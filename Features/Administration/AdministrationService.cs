using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using RentRight.Application.Abstractions;
using RentRight.Authorization;
using RentRight.Features.Administration.Contracts;

namespace RentRight.Features.Administration;

public sealed class AdministrationService(
    IAdministrationRoleStore roleStore,
    ICurrentUser currentUser) : IAdministrationService
{
    private const string AdministratorRole = AppRoles.Admin;
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

        try
        {
            await using var transaction = await roleStore.BeginSerializableTransactionAsync(cancellationToken);
            // Cookie claims may have been revoked while this request waited for the lock.
            if (string.IsNullOrWhiteSpace(currentUser.UserId) ||
                !await roleStore.IsUsableAdministratorAsync(currentUser.UserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserRolesOutcome.Forbidden();
            }
            var user = await roleStore.GetUserRoleStateAsync(command.UserId, cancellationToken);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserRolesOutcome.UserNotFound();
            }

            var storedRoles = await roleStore.GetStoredRoleNamesAsync(cancellationToken);
            var canonicalRoles = storedRoles.Where(role =>
                role is AppRoles.Admin or AppRoles.Landlord or AppRoles.Tenant).ToDictionary(role => role, RoleComparer);
            var requestedRoles = command.SelectedRoles
                .Select(role => role ?? string.Empty)
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

                if (administratorCount - (user.IsUsable ? 1 : 0) < 1)
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

            if (rolesToAdd.Length > 0 || rolesToRemove.Length > 0)
            {
                var stampResult = await roleStore.UpdateSecurityStampAsync(user.UserId, cancellationToken);
                if (!stampResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return UpdateUserRolesOutcome.IdentityFailure(stampResult.Errors);
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return UpdateUserRolesOutcome.Success(user.Email);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException { Number: 1205 or 51006 })
        {
            return UpdateUserRolesOutcome.ConcurrentUpdate();
        }
    }

    private ListAdministrationUsersOutcome? GetListAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return ListAdministrationUsersOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : ListAdministrationUsersOutcome.Forbidden();
    }

    private UpdateUserRolesOutcome? GetUpdateAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return UpdateUserRolesOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : UpdateUserRolesOutcome.Forbidden();
    }
}
