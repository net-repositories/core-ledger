using CoreLedger.Application.Abstractions;
using CoreLedger.Application.DTOs;
using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Exceptions;
using CoreLedger.Domain.Repositories;

namespace CoreLedger.Application.Services;

public class AccountService(
    IAccountRepository accountRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    ICurrentUserService currentUserService)
{
    private static readonly TimeSpan AccountCacheTtl = TimeSpan.FromMinutes(5);
    public static string GetCacheKey(Guid accountId) => $"account:{accountId}";

    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        var currentUserId = currentUserService.GetRequiredUserId();
        if (request.UserId != currentUserId)
            throw new ForbiddenException("You can only open accounts for yourself.");

        var user = await userRepository.GetByIdAsync(request.UserId, ct)
            ?? throw new NotFoundException($"User with id '{request.UserId}' not found.");

        var account = new Account(user.Id, request.Currency);
        await accountRepository.AddAsync(account, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var response = new AccountResponse(account.Id, account.UserId, account.Currency, account.Balance, account.Status);
        await cacheService.SetAsync(GetCacheKey(account.Id), response, AccountCacheTtl, ct);

        return response;
    }

    public async Task<AccountResponse?> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
    {
        var currentUserId = currentUserService.GetRequiredUserId();
        var cacheKey = GetCacheKey(id);

        var cached = await cacheService.GetAsync<AccountResponse>(cacheKey, ct);
        if (cached is not null)
        {
            if (cached.UserId != currentUserId)
                throw new ForbiddenException("Access to this account is denied.");

            return cached;
        }

        var account = await accountRepository.GetByIdAsync(id, ct);
        if (account is null) return null;

        if (account.UserId != currentUserId)
            throw new ForbiddenException("Access to this account is denied.");

        var response = new AccountResponse(account.Id, account.UserId, account.Currency, account.Balance, account.Status);
        await cacheService.SetAsync(cacheKey, response, AccountCacheTtl, ct);

        return response;
    }
}
