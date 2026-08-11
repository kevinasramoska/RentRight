using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentRight.Data;
using RentRight.Features.Administration.Contracts;
using RentRight.Models;

namespace RentRight.Features.Administration;

public sealed class IdentityAdministrationRoleStore(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : IAdministrationRoleStore
{
    private readonly Dictionary<string, ApplicationUser> _loadedUsers =
        new(StringComparer.Ordinal);

    public async Task<AdministrationUsersPage> GetUsersPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalUsers = await dbContext.Users
            .AsNoTracking()
            .CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalUsers / (double)pageSize));
        var effectivePage = Math.Min(page, totalPages);

        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                UserId = user.Id,
                user.FullName,
                Email = user.Email ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var userIds = users.Select(user => user.UserId).ToArray();
        var roleAssignments = userIds.Length == 0
            ? []
            : await (
                from userRole in dbContext.UserRoles.AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                orderby role.Name
                select new
                {
                    userRole.UserId,
                    RoleName = role.Name!
                })
                .ToListAsync(cancellationToken);

        var rolesByUser = roleAssignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(assignment => assignment.RoleName)
                    .ToArray());
        var rows = users
            .Select(user => new AdministrationUserRow(
                user.UserId,
                user.FullName,
                user.Email,
                rolesByUser.GetValueOrDefault(user.UserId, [])))
            .ToArray();
        var availableRoles = await GetStoredRoleNamesAsync(cancellationToken);

        return new AdministrationUsersPage(
            rows,
            availableRoles,
            effectivePage,
            pageSize,
            totalUsers);
    }

    public async Task<IReadOnlyList<string>> GetStoredRoleNamesAsync(
        CancellationToken cancellationToken) =>
        await roleManager.Roles
            .AsNoTracking()
            .Where(role => role.Name != null)
            .OrderBy(role => role.Name)
            .Select(role => role.Name!)
            .ToListAsync(cancellationToken);

    public async Task<AdministrationUserRoleState?> GetUserRoleStateAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _loadedUsers[user.Id] = user;
        var roles = await userManager.GetRolesAsync(user);
        cancellationToken.ThrowIfCancellationRequested();

        return new AdministrationUserRoleState(
            user.Id,
            user.Email ?? user.UserName ?? user.Id,
            roles.ToArray());
    }

    public async Task<int> CountUsersInRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        var normalizedRoleName = roleManager.NormalizeKey(roleName);
        var roleId = await dbContext.Roles
            .Where(role => role.NormalizedName == normalizedRoleName)
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);

        return await dbContext.UserRoles
            .CountAsync(userRole => userRole.RoleId == roleId, cancellationToken);
    }

    public async Task<AdministrationIdentityOperation> AddToRolesAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var user = await GetLoadedUserAsync(userId, cancellationToken);
        var result = await userManager.AddToRolesAsync(user, roles);
        cancellationToken.ThrowIfCancellationRequested();
        return ToOperation(result);
    }

    public async Task<AdministrationIdentityOperation> RemoveFromRolesAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var user = await GetLoadedUserAsync(userId, cancellationToken);
        var result = await userManager.RemoveFromRolesAsync(user, roles);
        cancellationToken.ThrowIfCancellationRequested();
        return ToOperation(result);
    }

    public async Task<IAdministrationRoleTransaction> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        return new AdministrationRoleTransaction(transaction);
    }

    private async Task<ApplicationUser> GetLoadedUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (_loadedUsers.TryGetValue(userId, out var loadedUser))
        {
            return loadedUser;
        }

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("The role-update user is no longer available.");
        cancellationToken.ThrowIfCancellationRequested();
        _loadedUsers[user.Id] = user;
        return user;
    }

    private static AdministrationIdentityOperation ToOperation(IdentityResult result) =>
        result.Succeeded
            ? AdministrationIdentityOperation.Success()
            : AdministrationIdentityOperation.Failure(
                result.Errors.Select(error => error.Description).ToArray());

    private sealed class AdministrationRoleTransaction(IDbContextTransaction transaction)
        : IAdministrationRoleTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) =>
            transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
