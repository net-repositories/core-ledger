using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreLedger.Application.DTOs;
using CoreLedger.Domain.Enums;

namespace CoreLedger.IntegrationTests;

public static class HttpClientExtensions
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<(Guid UserId, Guid AccountId)> CreateAuthenticatedUserWithAccountAsync(
        this HttpClient client,
        string emailPrefix,
        Currency currency = Currency.RUB)
    {
        var email = $"{emailPrefix}_{Guid.NewGuid():N}@test.com";

        // 1. Регистрация пользователя
        var createUserRes = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(email, $"User {emailPrefix}"),
            JsonOptions);
        createUserRes.EnsureSuccessStatusCode();
        var user = await createUserRes.Content.ReadFromJsonAsync<UserResponse>(JsonOptions);

        // 2. Получение JWT токена
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email), JsonOptions);
        loginRes.EnsureSuccessStatusCode();
        var login = await loginRes.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);

        // 3. Установка Bearer-токена
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        // 4. Открытие счета
        var createAccRes = await client.PostAsJsonAsync(
            "/api/accounts",
            new CreateAccountRequest(user!.Id, currency),
            JsonOptions);
        createAccRes.EnsureSuccessStatusCode();
        var account = await createAccRes.Content.ReadFromJsonAsync<AccountResponse>(JsonOptions);

        return (user.Id, account!.Id);
    }
}
