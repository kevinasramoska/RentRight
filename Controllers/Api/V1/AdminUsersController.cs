using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Authorization;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;

namespace RentRight.Controllers.Api.V1;

[Authorize(Policy = RentalPolicies.AdministerRoles)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUsersController(IAdministrationService administration) : ApiV1ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdministrationUsersPage>(200)]
    public async Task<IActionResult> List([FromQuery] ApiPageRequest query, CancellationToken cancellationToken)
    {
        var result = await administration.ListUsersAsync(new(query.Page, query.PageSize), cancellationToken);
        return result.Status == ListAdministrationUsersStatus.Success
            ? Ok(result.Page) : ApiProblem(403, ApiErrorCodes.AccessDenied);
    }

    [HttpPut("roles")]
    [ServiceFilter(typeof(RecentAuthenticationFilter))]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> UpdateRoles(UpdateWorkspaceRolesRequest request, CancellationToken cancellationToken)
    {
        var result = await administration.UpdateUserRolesAsync(new(request.UserId, request.SelectedRoles), cancellationToken);
        return result.Status switch
        {
            UpdateUserRolesStatus.Success => NoContent(),
            UpdateUserRolesStatus.Unauthenticated => ApiProblem(401, ApiErrorCodes.AuthenticationRequired),
            UpdateUserRolesStatus.Forbidden => ApiProblem(403, ApiErrorCodes.AccessDenied),
            UpdateUserRolesStatus.UserNotFound => ApiProblem(404, ApiErrorCodes.ResourceNotFound),
            UpdateUserRolesStatus.InvalidRoles => ApiValidationProblem(new Dictionary<string, string[]> { ["selectedRoles"] = ["Select only supported roles."] }),
            UpdateUserRolesStatus.LastAdministrator => ApiProblem(409, "last_administrator"),
            UpdateUserRolesStatus.ConcurrentUpdate => ApiProblem(409, ApiErrorCodes.ResourceConflict),
            _ => ApiProblem(503, "service_unavailable")
        };
    }
}

public sealed class UpdateWorkspaceRolesRequest
{
    [Required, StringLength(450)] public string UserId { get; init; } = string.Empty;
    [Required, MaxLength(3)] public string[] SelectedRoles { get; init; } = [];
}
