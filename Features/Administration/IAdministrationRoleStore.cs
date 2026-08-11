using RentRight.Features.Administration.Contracts;

namespace RentRight.Features.Administration;

public interface IAdministrationRoleStore
{
    Task<AdministrationUsersPage> GetUsersPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetStoredRoleNamesAsync(CancellationToken cancellationToken);

    Task<AdministrationUserRoleState?> GetUserRoleStateAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<int> CountUsersInRoleAsync(string roleName, CancellationToken cancellationToken);

    Task<AdministrationIdentityOperation> AddToRolesAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task<AdministrationIdentityOperation> RemoveFromRolesAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task<IAdministrationRoleTransaction> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken);
}

public sealed record AdministrationUserRoleState(
    string UserId,
    string Email,
    IReadOnlyList<string> Roles);

public sealed record AdministrationIdentityOperation(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static AdministrationIdentityOperation Success() => new(true, []);

    public static AdministrationIdentityOperation Failure(params string[] errors) =>
        new(false, errors);
}

public interface IAdministrationRoleTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}
