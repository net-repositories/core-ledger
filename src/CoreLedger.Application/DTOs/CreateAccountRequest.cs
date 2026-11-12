using CoreLedger.Domain.Enums;

namespace CoreLedger.Application.DTOs;

public record CreateAccountRequest(Guid UserId, Currency Currency);
