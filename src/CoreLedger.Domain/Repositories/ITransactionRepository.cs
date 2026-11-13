using CoreLedger.Domain.Entities;

namespace CoreLedger.Domain.Repositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
}