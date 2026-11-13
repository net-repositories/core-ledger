namespace CoreLedger.Domain.Exceptions;

public class DuplicateIdempotencyKeyException : Exception
{
    public Guid IdempotencyKey { get; }
    public DuplicateIdempotencyKeyException(Guid key, Exception inner)
        : base($"Transaction with idempotency key '{key}' already exists.", inner)
    {
        IdempotencyKey = key;
    }
}