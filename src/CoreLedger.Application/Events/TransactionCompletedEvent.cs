using CoreLedger.Domain.Enums;

namespace CoreLedger.Application.Events;

public record TransactionCompletedEvent(
    Guid TransactionId,
    Guid? FromAccountId,
    Guid? ToAccountId,
    decimal Amount,
    Currency Currency,
    TransactionType Type,
    DateTime OccurredAt);
