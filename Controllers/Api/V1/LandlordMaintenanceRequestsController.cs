using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Authorization;
using RentRight.Features.Maintenance;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;

namespace RentRight.Controllers.Api.V1;

[Authorize(Policy = RentalPolicies.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
public sealed class LandlordMaintenanceRequestsController(ILandlordMaintenanceService queries, IMaintenanceCommandService commands) : ApiV1ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiPage<MaintenanceQueueItem>>(200)]
    public async Task<IActionResult> List([FromQuery] MaintenanceQueueQuery query, CancellationToken cancellationToken)
    {
        var result = await queries.ListAsync(query, cancellationToken);
        return result.Status == TenantMaintenanceStatus.Success
            ? Ok(new ApiPage<MaintenanceQueueItem>(result.Value!.Items, query.Page, query.PageSize, result.Value.TotalCount))
            : Failure(result.Status);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<MaintenanceTriageDetail>(200)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await queries.GetAsync(id, cancellationToken);
        return result.Status == TenantMaintenanceStatus.Success ? Ok(result.Value) : Failure(result.Status);
    }

    [HttpPost("{id:int}/triage")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Update(int id, MaintenanceTriageInput request, CancellationToken cancellationToken)
    {
        var outcome = await commands.UpdateStatusAsync(new(id, id, request.NewStatus, request.ExpectedRevision,
            request.Priority, request.Note), cancellationToken);
        return outcome.Status switch
        {
            MaintenanceOutcomeStatus.Success => NoContent(),
            MaintenanceOutcomeStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            MaintenanceOutcomeStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            MaintenanceOutcomeStatus.NotFound or MaintenanceOutcomeStatus.ConcealedNotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            MaintenanceOutcomeStatus.Conflict => ApiProblem(409, ApiErrorCodes.ResourceConflict),
            MaintenanceOutcomeStatus.Unavailable => ApiProblem(503, "service_unavailable"),
            _ => ApiValidationProblem((outcome.Errors ?? []).GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))
        };
    }

    private IActionResult Failure(TenantMaintenanceStatus status) => status switch
    {
        TenantMaintenanceStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
        TenantMaintenanceStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
        TenantMaintenanceStatus.NotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
        TenantMaintenanceStatus.Invalid => ApiValidationProblem(new Dictionary<string, string[]> { ["filters"] = ["Supply valid paging, status, property and submitted date filters."] }),
        _ => ApiProblem(503, "service_unavailable")
    };
}

public sealed class MaintenanceTriageInput
{
    [Required, Range(0, long.MaxValue)] public long? ExpectedRevision { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<MaintenanceRequestStatus>))]
    [EnumDataType(typeof(MaintenanceRequestStatus))] public MaintenanceRequestStatus? NewStatus { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<MaintenancePriority>))]
    [EnumDataType(typeof(MaintenancePriority))] public MaintenancePriority? Priority { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
}
