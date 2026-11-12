using CoreLedger.Domain.Enums;
using CoreLedger.Domain.Exceptions;

namespace CoreLedger.Domain.Entities;

public class Account
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Currency Currency { get; private set; }
    public decimal Balance { get; private set; }
    public AccountStatus Status { get; private set; }
    public uint RowVersion { get; private set; } // Маппится на системный xmin в PostgreSQL

    public User User { get; private set; } = default!;

    private Account() { }

    public Account(Guid userId, Currency currency)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");

        Id = Guid.NewGuid();
        UserId = userId;
        Currency = currency;
        Balance = 0m;
        Status = AccountStatus.Active;
    }
}
