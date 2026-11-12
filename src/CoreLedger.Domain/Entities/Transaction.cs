using CoreLedger.Domain.Enums;
using CoreLedger.Domain.Exceptions;

namespace CoreLedger.Domain.Entities;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public Guid? FromAccountId { get; private set; }
    public Guid? ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public TransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Transaction() { }

    public Transaction(Guid idempotencyKey, Guid? fromAccountId, Guid? toAccountId, decimal amount, TransactionType type)
    {
        if (amount <= 0) throw new DomainException("Amount must be greater than zero.");

        Id = Guid.NewGuid();
        IdempotencyKey = idempotencyKey;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        Amount = amount;
        Type = type;
        Status = TransactionStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }
}
