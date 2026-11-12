using CoreLedger.Domain.Enums;

namespace CoreLedger.Application.DTOs;

public record AccountResponse(Guid Id, Guid UserId, Currency Currency, decimal Balance, AccountStatus Status);
