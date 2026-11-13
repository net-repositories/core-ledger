namespace CoreLedger.Application.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid GetRequiredUserId();
}
