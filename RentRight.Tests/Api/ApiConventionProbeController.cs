using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Controllers.Api.V1;
using RentRight.Features.Properties.Contracts;

namespace RentRight.Tests.Api;

public sealed record ApiConventionProbeItem(
    int ItemId,
    string DisplayName,
    DateTime UpdatedUtc,
    bool RequestAbortedObserved);

public sealed class ApiConventionValidationInput
{
    [Required]
    [StringLength(20)]
    public string? DisplayName { get; init; }
}

public sealed class ApiConventionProbeController : ApiV1ControllerBase
{
    [HttpGet("sample")]
    [AllowAnonymous]
    [ProducesResponseType<ApiPage<ApiConventionProbeItem>>(StatusCodes.Status200OK)]
    public ActionResult<ApiPage<ApiConventionProbeItem>> Sample(
        [FromQuery] ApiPageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        ApiConventionProbeItem[] items =
        [
            new(
                7,
                "Sample",
                new DateTime(2026, 8, 11, 12, 30, 0, DateTimeKind.Utc),
                cancellationToken == HttpContext.RequestAborted)
        ];

        return Ok(new ApiPage<ApiConventionProbeItem>(
            items,
            pageRequest.Page,
            pageRequest.PageSize,
            1));
    }

    [HttpPost("validate")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult Validate(ApiConventionValidationInput input) => Ok(input);

    [HttpGet("outcomes/{status}")]
    [AllowAnonymous]
    public IActionResult Outcome(PropertyOperationStatus status)
    {
        var outcome = status switch
        {
            PropertyOperationStatus.Unauthenticated =>
                PropertyMutationOutcome.Unauthenticated(),
            PropertyOperationStatus.Forbidden => PropertyMutationOutcome.Forbidden(),
            PropertyOperationStatus.NotFound => PropertyMutationOutcome.NotFound(),
            PropertyOperationStatus.ValidationFailed =>
                PropertyMutationOutcome.ValidationFailed(
                    new Dictionary<string, string[]>
                    {
                        [nameof(ApiConventionValidationInput.DisplayName)] =
                            ["Display name is invalid."]
                    }),
            PropertyOperationStatus.Conflict => PropertyMutationOutcome.Conflict(),
            PropertyOperationStatus.HasHistory => PropertyMutationOutcome.HasHistory(),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        return MapPropertyMutationFailure(outcome);
    }

    [HttpGet("secure")]
    [Authorize]
    public IActionResult Secure() => Ok(new { Authenticated = true });

    [HttpPost("secure")]
    [Authorize]
    public IActionResult SecurePost() => Ok(new { Accepted = true });

    [HttpGet("exception")]
    [AllowAnonymous]
    public IActionResult Exception() => throw new InvalidOperationException(
        "SQL provider detail marker; credential detail marker; internal frame marker.");
}
