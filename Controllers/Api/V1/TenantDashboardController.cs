using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;

namespace RentRight.Controllers.Api.V1;

[Authorize(Roles = "Tenant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantDashboardController(ITenantDashboardQueryService service) : ApiV1ControllerBase
{
    [HttpGet]
    [ProducesResponseType<TenantDashboardResponse>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        TenantDashboardOutcome result;
        try { result = await service.QueryAsync(cancellationToken); }
        catch (Exception exception) when (exception is DbException or DbUpdateException or TimeoutException
            || exception is InvalidOperationException { InnerException: DbException or TimeoutException })
        {
            return ApiProblem(503, "service_unavailable");
        }
        return result.Status switch
        {
            DashboardQueryStatus.Success => Ok(new TenantDashboardResponse(
                result.Dashboard!.Tenancies.Select(t => new TenantHomeResponse(
                    t.PropertyTitle, t.PropertyAddress, t.StartDate, t.EndDate)).ToArray(),
                result.Dashboard.Counts,
                result.Dashboard.RecentMaintenanceRequests.Select(r => new TenantRecentRequestResponse(
                    r.Id, r.PropertyTitle, DateTime.SpecifyKind(r.DateSubmitted, DateTimeKind.Utc), r.Status.ToString())).ToArray())),
            DashboardQueryStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            DashboardQueryStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            _ => throw new InvalidOperationException("Unknown tenant dashboard outcome.")
        };
    }
}

public sealed record TenantHomeResponse(string PropertyTitle, string PropertyAddress, DateOnly StartDate, DateOnly? EndDate);
public sealed record TenantRecentRequestResponse(int Id, string PropertyTitle, DateTime SubmittedUtc, string Status);
public sealed record TenantDashboardResponse(IReadOnlyList<TenantHomeResponse> Tenancies,
    TenantMaintenanceCounts Counts, IReadOnlyList<TenantRecentRequestResponse> RecentMaintenanceRequests);
