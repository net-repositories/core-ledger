using CoreLedger.Api.Middleware;
using CoreLedger.Api.Security;
using CoreLedger.Application.Abstractions;
using CoreLedger.Application.Consumers;
using CoreLedger.Application.DTOs;
using CoreLedger.Application.Services;
using CoreLedger.Domain.Exceptions;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Caching;
using CoreLedger.Infrastructure.Persistence;
using CoreLedger.Infrastructure.Repositories;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 1. PostgreSQL и репозитории
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();

// 2. Redis кэширование
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "CoreLedger:";
});
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// 3. JWT Аутентификация и CurrentUser
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SecretKey"]!))
        };
    });
builder.Services.AddAuthorization();

// 4. Rate Limiting (читаем лимит из конфигурации, по умолчанию 10)
var permitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 10;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("TransactionsRateLimit", httpContext =>
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit, // <-- Используем переменную здесь
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// 5. Сервисы Application
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();

// 6. MassTransit + RabbitMQ + Outbox
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TransactionCompletedConsumer>();

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConfigureEndpointsCallback((context, name, cfg) =>
    {
        cfg.UseEntityFrameworkOutbox<AppDbContext>(context);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitSection = builder.Configuration.GetSection("RabbitMQ");
        var rabbitPort = rabbitSection.GetValue<ushort?>("Port") ?? 5672;
        cfg.Host(rabbitSection["Host"] ?? "localhost", rabbitPort, rabbitSection["VirtualHost"] ?? "/", h =>
        {
            h.Username(rabbitSection["Username"] ?? "guest");
            h.Password(rabbitSection["Password"] ?? "guest");
        });

        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(10),
            intervalDelta: TimeSpan.FromSeconds(2)));

        cfg.ConfigureEndpoints(context);
    });
});

// 7. Swagger с кнопкой Authorize (Bearer JWT)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введите JWT токен"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Публичные эндпоинты (регистрация и получение токена)
app.MapPost("/api/users", async (CreateUserRequest request, UserService userService, CancellationToken ct) =>
{
    var result = await userService.CreateUserAsync(request, ct);
    return Results.Created($"/api/users/{result.Id}", result);
});

app.MapPost("/api/auth/login", async (LoginRequest request, IUserRepository userRepo, IJwtTokenGenerator jwtGen, CancellationToken ct) =>
{
    var user = await userRepo.GetByEmailAsync(request.Email, ct)
        ?? throw new NotFoundException($"User with email '{request.Email}' not found.");

    var token = jwtGen.GenerateToken(user.Id, user.Email);
    return Results.Ok(new LoginResponse(token, user.Id, user.Email));
});

// Защищенные эндпоинты счетов
var accountsGroup = app.MapGroup("/api/accounts").RequireAuthorization();

accountsGroup.MapPost("/", async (CreateAccountRequest request, AccountService accountService, CancellationToken ct) =>
{
    var result = await accountService.CreateAccountAsync(request, ct);
    return Results.Created($"/api/accounts/{result.Id}", result);
});

accountsGroup.MapGet("/{id:guid}", async (Guid id, AccountService accountService, CancellationToken ct) =>
{
    var result = await accountService.GetAccountByIdAsync(id, ct);
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

// Защищенные эндпоинты транзакций с Rate Limiting
var txGroup = app.MapGroup("/api/transactions")
    .RequireAuthorization()
    .RequireRateLimiting("TransactionsRateLimit");

txGroup.MapPost("/deposit", async (DepositRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.DepositAsync(request, ct);
    return Results.Ok(result);
});

txGroup.MapPost("/withdraw", async (WithdrawRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.WithdrawAsync(request, ct);
    return Results.Ok(result);
});

txGroup.MapPost("/transfer", async (TransferRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.TransferAsync(request, ct);
    return Results.Ok(result);
});

app.Run();

public record LoginRequest(string Email);
public record LoginResponse(string Token, Guid UserId, string Email);

public partial class Program { }