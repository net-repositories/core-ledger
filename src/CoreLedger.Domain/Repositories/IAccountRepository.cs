using CoreLedger.Domain.Entities;

namespace CoreLedger.Domain.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<List<Account>> GetByIdsForUpdateOrderedAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task AddAsync(Account account, CancellationToken ct = default);
}
