using System.Net;
using System.Net.Http.Json;
using CoreLedger.Application.DTOs;
using CoreLedger.Domain.Enums;

namespace CoreLedger.IntegrationTests;

public class TransactionConcurrencyTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Withdraw_ConcurrentRequests_ShouldPreventDoubleSpending()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (_, accountId) = await client.CreateAuthenticatedUserWithAccountAsync("double_spend");

        // Пополняем счет на 1000.00
        var depositRes = await client.PostAsJsonAsync(
            "/api/transactions/deposit",
            new DepositRequest(accountId, 1000.00m, Guid.NewGuid()),
            HttpClientExtensions.JsonOptions);
        depositRes.EnsureSuccessStatusCode();

        // Act: 10 одновременных запросов на списание по 200.00 с разными IdempotencyKey
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.PostAsJsonAsync(
                "/api/transactions/withdraw",
                new WithdrawRequest(accountId, 200.00m, Guid.NewGuid()),
                HttpClientExtensions.JsonOptions))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert: ровно 5 успешных списаний (200 OK) и 5 отказов по нехватке средств (422 UnprocessableEntity)
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var insufficientFundsCount = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        Assert.Equal(5, successCount);
        Assert.Equal(5, insufficientFundsCount);

        // Проверяем, что итоговый баланс строго 0.00 и не ушел в минус
        var accountRes = await client.GetAsync($"/api/accounts/{accountId}");
        accountRes.EnsureSuccessStatusCode();
        var account = await accountRes.Content.ReadFromJsonAsync<AccountResponse>(HttpClientExtensions.JsonOptions);

        Assert.NotNull(account);
        Assert.Equal(0.00m, account.Balance);
    }

    [Fact]
    public async Task Withdraw_ConcurrentDuplicateIdempotencyKey_ShouldChargeOnlyOnce()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (_, accountId) = await client.CreateAuthenticatedUserWithAccountAsync("idempotency");

        await client.PostAsJsonAsync(
            "/api/transactions/deposit",
            new DepositRequest(accountId, 1000.00m, Guid.NewGuid()),
            HttpClientExtensions.JsonOptions);

        var sharedIdempotencyKey = Guid.NewGuid();

        // Act: 5 одновременных запросов на списание 300.00 с ОДИНАКОВЫМ IdempotencyKey
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync(
                "/api/transactions/withdraw",
                new WithdrawRequest(accountId, 300.00m, sharedIdempotencyKey),
                HttpClientExtensions.JsonOptions))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert: все 5 запросов получили валидный 200 OK с одной и той же транзакцией
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var txResults = await Task.WhenAll(
            responses.Select(r => r.Content.ReadFromJsonAsync<TransactionResponse>(HttpClientExtensions.JsonOptions)));

        var distinctTransactionIds = txResults.Select(t => t!.Id).Distinct().ToList();
        Assert.Single(distinctTransactionIds); // Создана ровно 1 транзакция

        // Итоговый баланс должен быть 1000 - 300 = 700.00
        var account = await client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountId}",
            HttpClientExtensions.JsonOptions);

        Assert.NotNull(account);
        Assert.Equal(700.00m, account.Balance);
    }

    [Fact]
    public async Task Transfer_BidirectionalConcurrent_ShouldNotDeadlock()
    {
        // Arrange: создаем двух независимых пользователей (Вася и Петя)
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();

        var (_, accountA) = await clientA.CreateAuthenticatedUserWithAccountAsync("user_a");
        var (_, accountB) = await clientB.CreateAuthenticatedUserWithAccountAsync("user_b");

        // Пополняем оба счета на 1000.00
        await clientA.PostAsJsonAsync(
            "/api/transactions/deposit",
            new DepositRequest(accountA, 1000.00m, Guid.NewGuid()),
            HttpClientExtensions.JsonOptions);

        await clientB.PostAsJsonAsync(
            "/api/transactions/deposit",
            new DepositRequest(accountB, 1000.00m, Guid.NewGuid()),
            HttpClientExtensions.JsonOptions);

        // Act: 10 встречных переводов A -> B по 10.00 и 10 переводов B -> A по 10.00 одновременно
        var transfersAtoB = Enumerable.Range(0, 10)
            .Select(_ => clientA.PostAsJsonAsync(
                "/api/transactions/transfer",
                new TransferRequest(accountA, accountB, 10.00m, Guid.NewGuid()),
                HttpClientExtensions.JsonOptions));

        var transfersBtoA = Enumerable.Range(0, 10)
            .Select(_ => clientB.PostAsJsonAsync(
                "/api/transactions/transfer",
                new TransferRequest(accountB, accountA, 10.00m, Guid.NewGuid()),
                HttpClientExtensions.JsonOptions));

        var allResponses = await Task.WhenAll(transfersAtoB.Concat(transfersBtoA));

        // Assert: все 20 переводов завершились с 200 OK без единого Deadlock (40001)
        Assert.All(allResponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var finalAccountA = await clientA.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountA}",
            HttpClientExtensions.JsonOptions);
        var finalAccountB = await clientB.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountB}",
            HttpClientExtensions.JsonOptions);

        Assert.Equal(1000.00m, finalAccountA!.Balance);
        Assert.Equal(1000.00m, finalAccountB!.Balance);
    }

    [Fact]
    public async Task Deposit_ShouldInvalidateRedisCacheImmediately()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (_, accountId) = await client.CreateAuthenticatedUserWithAccountAsync("cache_test");

        // 1. Первый GET прогревает кэш в Redis (баланс = 0)
        var initialAccount = await client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountId}",
            HttpClientExtensions.JsonOptions);
        Assert.Equal(0.00m, initialAccount!.Balance);

        // 2. Пополняем счет на 500.00
        var depositRes = await client.PostAsJsonAsync(
            "/api/transactions/deposit",
            new DepositRequest(accountId, 500.00m, Guid.NewGuid()),
            HttpClientExtensions.JsonOptions);
        depositRes.EnsureSuccessStatusCode();

        // 3. Сразу делаем GET и проверяем, что вернулось актуальное значение 500.00, а не устаревший 0 из Redis
        var updatedAccount = await client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountId}",
            HttpClientExtensions.JsonOptions);

        Assert.NotNull(updatedAccount);
        Assert.Equal(500.00m, updatedAccount.Balance);
    }
}
