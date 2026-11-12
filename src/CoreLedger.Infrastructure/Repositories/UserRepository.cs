using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Repositories;
using CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreLedger.Infrastructure.Repositories;

public class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        dbContext.Users.AnyAsync(x => x.Email == email.Trim().ToLowerInvariant(), ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await dbContext.Users.AddAsync(user, ct);
}
