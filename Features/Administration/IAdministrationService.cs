using RentRight.Features.Administration.Contracts;

namespace RentRight.Features.Administration;

public interface IAdministrationService
{
    Task<ListAdministrationUsersOutcome> ListUsersAsync(
        AdministrationUsersQuery query,
        CancellationToken cancellationToken);

    Task<UpdateUserRolesOutcome> UpdateUserRolesAsync(
        UpdateUserRolesCommand command,
        CancellationToken cancellationToken);
}
