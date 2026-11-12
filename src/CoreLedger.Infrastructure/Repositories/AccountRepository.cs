using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreLedger.Infrastructure.Repositories;

public class AccountRepository(AppDbContext dbContext) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(Account account, CancellationToken ct = default) =>
        await dbContext.Accounts.AddAsync(account, ct);
}
