using CoreLedger.Application.Abstractions;
using CoreLedger.Application.DTOs;
using CoreLedger.Application.Events;
using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Enums;
using CoreLedger.Domain.Exceptions;
using CoreLedger.Domain.Repositories;
using MassTransit;

namespace CoreLedger.Application.Services;

public class TransactionService(
    IAccountRepository accountRepository,
    ITransactionRepository transactionRepository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    ICacheService cacheService,
    ICurrentUserService currentUserService)
{
    public async Task<TransactionResponse> DepositAsync(DepositRequest request, CancellationToken ct = default)
    {
        var existing = await GetCompletedTransactionAsync(request.IdempotencyKey, ct);
        if (existing is not null) return existing;

        try
        {
            await using var dbTx = await unitOfWork.BeginTransactionAsync(ct);

            var account = await accountRepository.GetByIdForUpdateAsync(request.AccountId, ct)
                ?? throw new NotFoundException($"Account '{request.AccountId}' not found.");

            var transaction = new Transaction(
                request.IdempotencyKey,
                fromAccountId: null,
                toAccountId: account.Id,
                request.Amount,
                TransactionType.Deposit);

            account.Deposit(request.Amount);
            transaction.MarkAsCompleted();

            await transactionRepository.AddAsync(transaction, ct);

            await publishEndpoint.Publish(new TransactionCompletedEvent(
                transaction.Id,
                transaction.FromAccountId,
                transaction.ToAccountId,
                transaction.Amount,
                account.Currency,
                transaction.Type,
                transaction.CreatedAt), ct);

            await unitOfWork.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            // Инвалидация кэша строго ПОСЛЕ коммита в БД
            await cacheService.RemoveAsync(AccountService.GetCacheKey(account.Id), ct);

            return MapToResponse(transaction);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            return await ResolveConcurrentDuplicateAsync(request.IdempotencyKey, ct);
        }
    }

    public async Task<TransactionResponse> WithdrawAsync(WithdrawRequest request, CancellationToken ct = default)
    {
        var currentUserId = currentUserService.GetRequiredUserId();

        var existing = await GetCompletedTransactionAsync(request.IdempotencyKey, ct);
        if (existing is not null) return existing;

        try
        {
            await using var dbTx = await unitOfWork.BeginTransactionAsync(ct);

            var account = await accountRepository.GetByIdForUpdateAsync(request.AccountId, ct)
                ?? throw new NotFoundException($"Account '{request.AccountId}' not found.");

            // Проверка владельца счета (Ownership)
            if (account.UserId != currentUserId)
                throw new ForbiddenException("You are not allowed to withdraw funds from another user's account.");

            var transaction = new Transaction(
                request.IdempotencyKey,
                fromAccountId: account.Id,
                toAccountId: null,
                request.Amount,
                TransactionType.Withdrawal);

            account.Withdraw(request.Amount);
            transaction.MarkAsCompleted();

            await transactionRepository.AddAsync(transaction, ct);

            await publishEndpoint.Publish(new TransactionCompletedEvent(
                transaction.Id,
                transaction.FromAccountId,
                transaction.ToAccountId,
                transaction.Amount,
                account.Currency,
                transaction.Type,
                transaction.CreatedAt), ct);

            await unitOfWork.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            await cacheService.RemoveAsync(AccountService.GetCacheKey(account.Id), ct);

            return MapToResponse(transaction);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            return await ResolveConcurrentDuplicateAsync(request.IdempotencyKey, ct);
        }
    }

    public async Task<TransactionResponse> TransferAsync(TransferRequest request, CancellationToken ct = default)
    {
        var currentUserId = currentUserService.GetRequiredUserId();

        if (request.FromAccountId == request.ToAccountId)
            throw new DomainException("Cannot transfer funds to the same account.");

        var existing = await GetCompletedTransactionAsync(request.IdempotencyKey, ct);
        if (existing is not null) return existing;

        try
        {
            await using var dbTx = await unitOfWork.BeginTransactionAsync(ct);

            var lockedAccounts = await accountRepository.GetByIdsForUpdateOrderedAsync(
                [request.FromAccountId, request.ToAccountId], ct);

            var fromAccount = lockedAccounts.FirstOrDefault(a => a.Id == request.FromAccountId)
                ?? throw new NotFoundException($"Source account '{request.FromAccountId}' not found.");

            // Проверка владельца счета списания (Ownership)
            if (fromAccount.UserId != currentUserId)
                throw new ForbiddenException("You are not allowed to transfer funds from another user's account.");

            var toAccount = lockedAccounts.FirstOrDefault(a => a.Id == request.ToAccountId)
                ?? throw new NotFoundException($"Destination account '{request.ToAccountId}' not found.");

            if (fromAccount.Currency != toAccount.Currency)
                throw new DomainException(
                    $"Currency mismatch: cannot transfer from {fromAccount.Currency} to {toAccount.Currency}.");

            var transaction = new Transaction(
                request.IdempotencyKey,
                fromAccountId: fromAccount.Id,
                toAccountId: toAccount.Id,
                request.Amount,
                TransactionType.Transfer);

            fromAccount.Withdraw(request.Amount);
            toAccount.Deposit(request.Amount);
            transaction.MarkAsCompleted();

            await transactionRepository.AddAsync(transaction, ct);

            await publishEndpoint.Publish(new TransactionCompletedEvent(
                transaction.Id,
                transaction.FromAccountId,
                transaction.ToAccountId,
                transaction.Amount,
                fromAccount.Currency,
                transaction.Type,
                transaction.CreatedAt), ct);

            await unitOfWork.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            // Инвалидируем оба затронутых счета
            await cacheService.RemoveAsync(AccountService.GetCacheKey(fromAccount.Id), ct);
            await cacheService.RemoveAsync(AccountService.GetCacheKey(toAccount.Id), ct);

            return MapToResponse(transaction);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            return await ResolveConcurrentDuplicateAsync(request.IdempotencyKey, ct);
        }
    }

    private async Task<TransactionResponse?> GetCompletedTransactionAsync(Guid idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey == Guid.Empty)
            throw new DomainException("IdempotencyKey cannot be empty.");

        var existing = await transactionRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existing is null) return null;

        if (existing.Status == TransactionStatus.Completed)
            return MapToResponse(existing);

        throw new DomainException($"Transaction with key '{idempotencyKey}' is currently in status '{existing.Status}'.");
    }

    private async Task<TransactionResponse> ResolveConcurrentDuplicateAsync(Guid idempotencyKey, CancellationToken ct)
    {
        // Небольшая пауза, если конкурирующий поток еще завершает коммит
        for (int i = 0; i < 5; i++)
        {
            var existing = await transactionRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
            if (existing is { Status: TransactionStatus.Completed })
                return MapToResponse(existing);

            await Task.Delay(50, ct);
        }

        throw new DomainException($"Concurrent request with idempotency key '{idempotencyKey}' is still processing.");
    }

    private static TransactionResponse MapToResponse(Transaction tx) => new(
        tx.Id,
        tx.IdempotencyKey,
        tx.FromAccountId,
        tx.ToAccountId,
        tx.Amount,
        tx.Type,
        tx.Status,
        tx.CreatedAt);
}
