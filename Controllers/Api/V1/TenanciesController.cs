using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Controllers.Api.V1;

[Authorize(Roles = "Admin,Landlord,Tenant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenanciesController(ITenancyService service) : ApiV1ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiPage<TenancyResponse>>(200)]
    public async Task<IActionResult> List([FromQuery] ApiPageRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(request.Page, request.PageSize, cancellationToken);
        if (result.Status != TenancyQueryStatus.Success) return QueryFailure(result.Status);
        return Ok(new ApiPage<TenancyResponse>(result.Page!.Items.Select(ToResponse).ToArray(), request.Page, request.PageSize, result.Page.TotalCount));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<TenancyResponse>(200)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        if (result.Status != TenancyQueryStatus.Success) return QueryFailure(result.Status);
        return result.Tenancy is null ? ApiProblem(404, ApiErrorCodes.ResourceNotFound) : Ok(ToResponse(result.Tenancy));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Landlord")]
    [ProducesResponseType<InvitationResponse>(202)]
    public async Task<IActionResult> Invite(InviteTenancyRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(request.PropertyId, request.TenantIdentifier, request.StartDate, request.EndDate), cancellationToken);
        return result.Status switch
        {
            CreateTenancyStatus.Success => StatusCode(202, new InvitationResponse(result.Invitation!.Token, result.Invitation.ExpiresUtc)),
            CreateTenancyStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            CreateTenancyStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            CreateTenancyStatus.PropertyNotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            CreateTenancyStatus.Conflict => ApiProblem(409, ApiErrorCodes.ResourceConflict),
            CreateTenancyStatus.ValidationFailed => ApiValidationProblem(result.Errors!.GroupBy(e => e.Field == "TenantId" ? "TenantIdentifier" : e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray())),
            _ => throw new InvalidOperationException("Unknown invitation outcome.")
        };
    }

    [HttpPost("accept")]
    [Authorize(Roles = "Tenant")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Accept(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(new(request.Token), cancellationToken);
        return result.Status switch
        {
            AcceptTenancyStatus.Success => NoContent(),
            AcceptTenancyStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            AcceptTenancyStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            AcceptTenancyStatus.NotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            _ => ApiProblem(409, ApiErrorCodes.ResourceConflict)
        };
    }

    [HttpPost("{id:int}/end")]
    [Authorize(Roles = "Admin,Landlord")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> End(int id, CancellationToken cancellationToken)
    {
        var result = await service.EndAsync(id, cancellationToken);
        return result.Status switch
        {
            EndTenancyStatus.Success => NoContent(),
            EndTenancyStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            EndTenancyStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            EndTenancyStatus.NotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            _ => ApiProblem(409, ApiErrorCodes.ResourceConflict)
        };
    }

    private IActionResult QueryFailure(TenancyQueryStatus status) => status == TenancyQueryStatus.Unauthenticated
        ? ApiProblem(401, ApiErrorCodes.AuthenticationRequired) : ApiProblem(403, ApiErrorCodes.AccessDenied);
    private static TenancyResponse ToResponse(TenancyAdminRow row) => new(row.Id, row.PropertyTitle, row.PropertyAddress, row.StartDate, row.EndDate, row.Status.ToString());
}

public sealed class InviteTenancyRequest
{
    [Range(1, int.MaxValue)] public int PropertyId { get; init; }
    [Required, StringLength(256)] public string TenantIdentifier { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}
public sealed class AcceptInvitationRequest
{
    [Required, StringLength(64, MinimumLength = 64)] public string Token { get; init; } = string.Empty;
}
public sealed record InvitationResponse(string Token, DateTime ExpiresUtc);
public sealed record TenancyResponse(int Id, string PropertyTitle, string PropertyAddress, DateOnly StartDate, DateOnly? EndDate, string Status);
