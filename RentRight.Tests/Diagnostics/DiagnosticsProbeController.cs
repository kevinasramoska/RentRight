using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Controllers.Api.V1;
using RentRight.Integrations.Geocoding;

namespace RentRight.Tests.Diagnostics;

public sealed class DiagnosticsProbeController : ApiV1ControllerBase
{
    [HttpGet("items/{id}")]
    [AllowAnonymous]
    public IActionResult Read(string id) => Ok(new { Found = true });

    [HttpPost("echo")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult Echo([FromBody] Dictionary<string, string> input) => Ok(new { Accepted = true });

    [HttpGet("lookup")]
    [AllowAnonymous]
    public async Task<IActionResult> Lookup([FromServices] IGeocodingClient client, CancellationToken cancellationToken) =>
        Ok(new { Outcome = (await client.LookupAsync("private-address-marker", cancellationToken)).Status.ToString() });
}

[Route("diagnostics-probe/mvc")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class DiagnosticsMvcProbeController : Controller
{
    [HttpGet, HttpPost]
    public IActionResult Fail() => throw new InvalidOperationException("private-mvc-exception-marker");
}
