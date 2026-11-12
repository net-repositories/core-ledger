using System.Text.Json.Serialization;
using CoreLedger.Api.Middleware;
using CoreLedger.Application.DTOs;
using CoreLedger.Application.Services;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using CoreLedger.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Поддержка строковых Enum в JSON (RUB, USD, EUR)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Регистрация БД и репозиториев
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

// Регистрация сервисов Application
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AccountService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Эндпоинты
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

app.Run();
