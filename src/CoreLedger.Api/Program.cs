using System.Text.Json.Serialization;
using CoreLedger.Api.Middleware;
using CoreLedger.Application.DTOs;
using CoreLedger.Application.Services;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using CoreLedger.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using CoreLedger.Application.Consumers;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ProblemDetails + IExceptionHandler (RFC 7807)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// БД и репозитории
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();

// Сервисы Application
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TransactionCompletedConsumer>();

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // Подключаем Outbox/Inbox ко всем эндпоинтам консьюмеров
    x.AddConfigureEndpointsCallback((context, name, cfg) =>
    {
        cfg.UseEntityFrameworkOutbox<AppDbContext>(context);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitSection = builder.Configuration.GetSection("RabbitMQ");
        cfg.Host(rabbitSection["Host"] ?? "localhost", rabbitSection["VirtualHost"] ?? "/", h =>
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Пользователи и счета
app.MapPost("/api/users", async (CreateUserRequest request, UserService userService, CancellationToken ct) =>
{
    var result = await userService.CreateUserAsync(request, ct);
    return Results.Created($"/api/users/{result.Id}", result);
});

app.MapPost("/api/accounts", async (CreateAccountRequest request, AccountService accountService, CancellationToken ct) =>
{
    var result = await accountService.CreateAccountAsync(request, ct);
    return Results.Created($"/api/accounts/{result.Id}", result);
});

app.MapGet("/api/accounts/{id:guid}", async (Guid id, AccountService accountService, CancellationToken ct) =>
{
    var result = await accountService.GetAccountByIdAsync(id, ct);
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

// Финансовые транзакции
app.MapPost("/api/transactions/deposit", async (DepositRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.DepositAsync(request, ct);
    return Results.Ok(result);
});

app.MapPost("/api/transactions/withdraw", async (WithdrawRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.WithdrawAsync(request, ct);
    return Results.Ok(result);
});

app.MapPost("/api/transactions/transfer", async (TransferRequest request, TransactionService txService, CancellationToken ct) =>
{
    var result = await txService.TransferAsync(request, ct);
    return Results.Ok(result);
});

app.Run();
