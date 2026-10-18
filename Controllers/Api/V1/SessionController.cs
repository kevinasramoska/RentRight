using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using RentRight.Authorization;
using RentRight.Models;

namespace RentRight.Controllers.Api.V1;

public sealed record SessionResponse(bool IsAuthenticated, IReadOnlyList<string> Roles);
public sealed record AntiforgeryResponse(string RequestToken);

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SessionController : ApiV1ControllerBase
{
    [HttpGet]
    public ActionResult<SessionResponse> Get() => Ok(new SessionResponse(
        User.Identity?.IsAuthenticated == true,
        User.Identity?.IsAuthenticated == true
            ? new[] { AppRoles.Admin, AppRoles.Landlord, AppRoles.Tenant }.Where(User.IsInRole).ToArray()
            : []));

    [HttpGet("antiforgery")]
    public ActionResult<AntiforgeryResponse> Antiforgery([FromServices] IAntiforgery antiforgery)
        => Ok(new AntiforgeryResponse(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!));

    // Idempotent, same-origin, antiforgery-protected sign-out; never use a GET.
    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout([FromServices] SignInManager<ApplicationUser> signIn)
    {
        await signIn.SignOutAsync();
        return NoContent();
    }
}
