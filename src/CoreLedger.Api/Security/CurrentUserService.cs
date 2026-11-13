using System.Security.Claims;
using CoreLedger.Application.Abstractions;
using CoreLedger.Domain.Exceptions;

namespace CoreLedger.Api.Security;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public Guid GetRequiredUserId() =>
        UserId ?? throw new ForbiddenException("User is not authenticated.");
}
