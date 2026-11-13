using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreLedger.Infrastructure.Repositories;

public class AccountRepository(AppDbContext dbContext) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Accounts
            .FromSqlInterpolated($@"SELECT * FROM accounts WHERE ""Id"" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

    public Task<List<Account>> GetByIdsForUpdateOrderedAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var orderedIds = ids.Distinct().OrderBy(x => x).ToArray();

        return dbContext.Accounts
            .FromSqlInterpolated($@"SELECT * FROM accounts WHERE ""Id"" = ANY({orderedIds}) ORDER BY ""Id"" ASC FOR UPDATE")
            .ToListAsync(ct);
    }

    public async Task AddAsync(Account account, CancellationToken ct = default) =>
        await dbContext.Accounts.AddAsync(account, ct);
}
