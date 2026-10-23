using System.Security.Claims;

namespace Crivosistemas.Api.Common;

public static class CurrentUser
{
    public static Guid Id(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();
}
