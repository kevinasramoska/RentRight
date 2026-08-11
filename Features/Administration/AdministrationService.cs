using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Features.Administration.Contracts;

namespace RentRight.Features.Administration;

public sealed class AdministrationService(
    IAdministrationRoleStore roleStore,
    ICurrentUser currentUser) : IAdministrationService
{
    private const string AdministratorRole = "Admin";
    private static readonly StringComparer RoleComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<ListAdministrationUsersOutcome> ListUsersAsync(
        AdministrationUsersQuery query,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetListAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize,
            1,
            AdministrationUsersQuery.MaximumPageSize);

        var result = await roleStore.GetUsersPageAsync(page, pageSize, cancellationToken);
        return ListAdministrationUsersOutcome.Success(result);
    }

    public async Task<UpdateUserRolesOutcome> UpdateUserRolesAsync(
        UpdateUserRolesCommand command,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetUpdateAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var transaction =
            await roleStore.BeginSerializableTransactionAsync(cancellationToken);

        try
        {
            var user = await roleStore.GetUserRoleStateAsync(command.UserId, cancellationToken);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserRolesOutcome.UserNotFound();
            }

            var storedRoles = await roleStore.GetStoredRoleNamesAsync(cancellationToken);
            var canonicalRoles = storedRoles.ToDictionary(role => role, RoleComparer);
            var requestedRoles = command.SelectedRoles
                .Distinct(RoleComparer)
                .ToArray();
            var invalidRoles = requestedRoles
                .Where(role => !canonicalRoles.ContainsKey(role))
                .ToArray();

            if (invalidRoles.Length > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserRolesOutcome.InvalidRoles(invalidRoles);
            }

            var canonicalRequestedRoles = requestedRoles
                .Select(role => canonicalRoles[role])
                .ToHashSet(RoleComparer);
            var currentRoles = user.Roles.ToHashSet(RoleComparer);

            if (currentRoles.Contains(AdministratorRole) &&
                !canonicalRequestedRoles.Contains(AdministratorRole))
            {
                var storedAdministratorRole = canonicalRoles[AdministratorRole];
                var administratorCount = await roleStore.CountUsersInRoleAsync(
                    storedAdministratorRole,
                    cancellationToken);

                if (administratorCount <= 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return UpdateUserRolesOutcome.LastAdministrator();
                }
            }

            var rolesToAdd = canonicalRequestedRoles
                .Where(role => !currentRoles.Contains(role))
                .ToArray();
            var rolesToRemove = currentRoles
                .Where(role => !canonicalRequestedRoles.Contains(role))
                .ToArray();

            if (rolesToAdd.Length > 0)
            {
                var addResult = await roleStore.AddToRolesAsync(
                    user.UserId,
                    rolesToAdd,
                    cancellationToken);
                if (!addResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return UpdateUserRolesOutcome.IdentityFailure(addResult.Errors);
                }
            }

            if (rolesToRemove.Length > 0)
            {
                var removeResult = await roleStore.RemoveFromRolesAsync(
                    user.UserId,
                    rolesToRemove,
                    cancellationToken);
                if (!removeResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return UpdateUserRolesOutcome.IdentityFailure(removeResult.Errors);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return UpdateUserRolesOutcome.Success(user.Email);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UpdateUserRolesOutcome.ConcurrentUpdate();
        }
    }

    private ListAdministrationUsersOutcome? GetListAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return ListAdministrationUsersOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : ListAdministrationUsersOutcome.Forbidden();
    }

    private UpdateUserRolesOutcome? GetUpdateAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return UpdateUserRolesOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : UpdateUserRolesOutcome.Forbidden();
    }
}
