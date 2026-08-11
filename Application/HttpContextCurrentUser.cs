using System.Security.Claims;
using RentRight.Application.Abstractions;

namespace RentRight.Application;

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public bool IsInRole(string roleName) => Principal?.IsInRole(roleName) == true;
}
