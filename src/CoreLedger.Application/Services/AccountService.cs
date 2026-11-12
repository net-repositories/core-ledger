using CoreLedger.Application.DTOs;
using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Exceptions;
using CoreLedger.Domain.Repositories;

namespace CoreLedger.Application.Services;

public class AccountService(
    IAccountRepository accountRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, ct)
            ?? throw new DomainException($"User with id '{request.UserId}' not found.");

        var account = new Account(user.Id, request.Currency);
        await accountRepository.AddAsync(account, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new AccountResponse(account.Id, account.UserId, account.Currency, account.Balance, account.Status);
    }

    public async Task<AccountResponse?> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
    {
        var account = await accountRepository.GetByIdAsync(id, ct);
        if (account is null) return null;

        return new AccountResponse(account.Id, account.UserId, account.Currency, account.Balance, account.Status);
    }
}
