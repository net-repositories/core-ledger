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
    public uint RowVersion { get; private set; }

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

    public void Deposit(decimal amount)
    {
        EnsureActive();
        if (amount <= 0)
            throw new DomainException("Deposit amount must be greater than zero.");

        Balance += amount;
    }

    public void Withdraw(decimal amount)
    {
        EnsureActive();
        if (amount <= 0)
            throw new DomainException("Withdrawal amount must be greater than zero.");

        if (Balance < amount)
            throw new InsufficientFundsException(Id, Balance, amount);

        Balance -= amount;
    }

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
            throw new DomainException($"Account '{Id}' is not active (current status: {Status}).");
    }
}
