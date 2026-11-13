using CoreLedger.Domain.Enums;

namespace CoreLedger.Application.DTOs;

public record DepositRequest(Guid AccountId, decimal Amount, Guid IdempotencyKey);
public record WithdrawRequest(Guid AccountId, decimal Amount, Guid IdempotencyKey);
public record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, Guid IdempotencyKey);

public record TransactionResponse(
    Guid Id,
    Guid IdempotencyKey,
    Guid? FromAccountId,
    Guid? ToAccountId,
    decimal Amount,
    TransactionType Type,
    TransactionStatus Status,
    DateTime CreatedAt);
