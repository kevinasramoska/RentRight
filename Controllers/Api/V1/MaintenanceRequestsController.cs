using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Features.Maintenance;

namespace RentRight.Controllers.Api.V1;

[Authorize(Roles = "Tenant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
public sealed class MaintenanceRequestsController(ITenantMaintenanceService service) : ApiV1ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiPage<TenantMaintenanceItem>>(200)]
    public async Task<IActionResult> List([FromQuery] ApiPageRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(request.Page, request.PageSize, cancellationToken);
        return result.Status == TenantMaintenanceStatus.Success
            ? Ok(new ApiPage<TenantMaintenanceItem>(result.Value!.Items, request.Page, request.PageSize, result.Value.TotalCount))
            : Failure(result.Status);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<TenantMaintenanceDetail>(200)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result.Status == TenantMaintenanceStatus.Success ? Ok(result.Value) : Failure(result.Status);
    }

    [HttpPost]
    [ProducesResponseType<MaintenanceSubmissionResponse>(201)]
    public async Task<IActionResult> Create(MaintenanceSubmissionRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.Description, cancellationToken);
        return result.Status == TenantMaintenanceStatus.Success
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new MaintenanceSubmissionResponse(result.Value))
            : Failure(result.Status);
    }

    private IActionResult Failure(TenantMaintenanceStatus status) => status switch
    {
        TenantMaintenanceStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
        TenantMaintenanceStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
        TenantMaintenanceStatus.NotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
        TenantMaintenanceStatus.NoActiveTenancy => ApiProblem(409, "no_active_tenancy"),
        TenantMaintenanceStatus.Conflict => ApiProblem(409, ApiErrorCodes.ResourceConflict),
        TenantMaintenanceStatus.Invalid => ApiValidationProblem(new Dictionary<string, string[]>
            { ["Description"] = ["Enter a description between 10 and 2000 characters after trimming."] }),
        TenantMaintenanceStatus.Unavailable => ApiProblem(503, "service_unavailable"),
        _ => throw new InvalidOperationException("Unknown maintenance outcome.")
    };
}

public sealed class MaintenanceSubmissionRequest
{
    [Required, StringLength(2000, MinimumLength = 10)]
    public string Description { get; init; } = string.Empty;
}
public sealed record MaintenanceSubmissionResponse(int Id);
