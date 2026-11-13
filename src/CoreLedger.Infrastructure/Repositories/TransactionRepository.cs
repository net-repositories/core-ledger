using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreLedger.Infrastructure.Repositories;

public class TransactionRepository(AppDbContext dbContext) : ITransactionRepository
{
    public Task<Transaction?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken ct = default) =>
        dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(Transaction transaction, CancellationToken ct = default) =>
        await dbContext.Transactions.AddAsync(transaction, ct);
}
