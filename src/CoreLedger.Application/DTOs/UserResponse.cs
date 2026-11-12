namespace CoreLedger.Application.DTOs;

public record UserResponse(Guid Id, string Email, string FullName, DateTime CreatedAt);
