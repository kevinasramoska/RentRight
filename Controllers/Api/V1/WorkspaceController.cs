using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Authorization;
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;
using RentRight.Features.Properties;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Controllers.Api.V1;

// Screen-specific projections. Commands still use the existing property/tenancy APIs.
[Authorize(Policy = RentalPolicies.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceController(IAdminDashboardQueryService adminDashboard,
    ILandlordDashboardQueryService landlordDashboard, IPropertyService properties,
    ITenancyService tenancies, IPropertyLocationService locations) : ApiV1ControllerBase
{
    [HttpGet("published-properties/{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType<WorkspacePublishedProperty>(200)]
    public async Task<IActionResult> PublishedProperty(int id, CancellationToken cancellationToken)
    {
        var result = await properties.GetDetailsAsync(id, cancellationToken);
        return result.Value is { } property
            ? Ok(new WorkspacePublishedProperty(property.Id, property.Title, property.Address, property.RentAmount,
                property.Description, property.Location, property.CanManage))
            : MapPropertyQueryFailure(result);
    }

    [HttpGet]
    [ProducesResponseType<WorkspaceSummary>(200)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Admin))
        {
            var result = await adminDashboard.QueryAsync(cancellationToken);
            return result.Status == DashboardQueryStatus.Success
                ? Ok(new WorkspaceSummary(result.Dashboard!.TotalUsers, result.Dashboard.TotalProperties, [], locations.IsEnabled))
                : ApiProblem(403, ApiErrorCodes.AccessDenied);
        }
        var landlord = await landlordDashboard.QueryAsync(cancellationToken);
        return landlord.Status == DashboardQueryStatus.Success
            ? Ok(new WorkspaceSummary(null, null, landlord.Dashboard!.Properties, locations.IsEnabled))
            : ApiProblem(403, ApiErrorCodes.AccessDenied);
    }

    [HttpGet("properties/{id:int}")]
    [ProducesResponseType<Features.Properties.Contracts.PropertyDetails>(200)]
    public async Task<IActionResult> Property(int id, CancellationToken cancellationToken)
    {
        var authorized = await properties.GetEditAsync(id, cancellationToken);
        if (authorized.Status != Features.Properties.Contracts.PropertyOperationStatus.Success)
            return MapPropertyQueryFailure(authorized, concealForbidden: true);
        var result = await properties.GetDetailsAsync(id, cancellationToken);
        return result.Value is { CanManage: true } detail ? Ok(detail) : ApiProblem(404, ApiErrorCodes.ResourceNotFound);
    }

    [HttpGet("tenancies")]
    [ProducesResponseType<ApiPage<WorkspaceTenancy>>(200)]
    public async Task<IActionResult> Tenancies([FromQuery] ApiPageRequest query, CancellationToken cancellationToken)
    {
        var result = await tenancies.ListAsync(query.Page, query.PageSize, cancellationToken);
        return result.Status == TenancyQueryStatus.Success
            ? Ok(new ApiPage<WorkspaceTenancy>(result.Page!.Items.Select(row => new WorkspaceTenancy(
                row.Id, row.PropertyTitle, row.PropertyAddress, row.TenantName, row.TenantEmail,
                row.StartDate, row.EndDate, row.Status.ToString(), row.Version)).ToArray(),
                query.Page, query.PageSize, result.Page.TotalCount))
            : ApiProblem(403, ApiErrorCodes.AccessDenied);
    }

    [HttpGet("invitation-form")]
    [ProducesResponseType<TenancyCreateForm>(200)]
    public async Task<IActionResult> InvitationForm(CancellationToken cancellationToken)
    {
        var result = await tenancies.GetCreateFormAsync(cancellationToken);
        return result.Status == TenancyQueryStatus.Success ? Ok(result.Form) : ApiProblem(403, ApiErrorCodes.AccessDenied);
    }

    [HttpPost("properties/{id:int}/location")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> RefreshLocation(int id, RefreshLocationRequest request, CancellationToken cancellationToken)
    {
        var result = await locations.RefreshAsync(id, request.ExpectedVersion, cancellationToken);
        return result switch
        {
            LocationRefreshStatus.Updated => NoContent(),
            LocationRefreshStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            LocationRefreshStatus.Forbidden or LocationRefreshStatus.NotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            LocationRefreshStatus.Conflict => ApiProblem(409, ApiErrorCodes.ResourceConflict),
            LocationRefreshStatus.InvalidVersion => ApiValidationProblem(new Dictionary<string, string[]> { ["expectedVersion"] = ["Reload the property and supply its version."] }),
            _ => ApiProblem(503, "location_unavailable")
        };
    }
}

public sealed record WorkspaceSummary(int? TotalUsers, int? TotalProperties,
    IReadOnlyList<LandlordPropertySummary> Properties, bool LocationLookupEnabled);
public sealed record WorkspacePublishedProperty(int Id, string Title, string Address, decimal RentAmount,
    string? Description, Features.Properties.Contracts.PropertyLocation? Location, bool CanManage);
public sealed record WorkspaceTenancy(int Id, string PropertyTitle, string PropertyAddress,
    string TenantName, string TenantEmail, DateOnly StartDate, DateOnly? EndDate, string Status, string? Version);
public sealed class RefreshLocationRequest
{
    [Required, StringLength(24)] public string ExpectedVersion { get; init; } = string.Empty;
}
