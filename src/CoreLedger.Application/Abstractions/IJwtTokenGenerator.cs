namespace CoreLedger.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string email);
}
