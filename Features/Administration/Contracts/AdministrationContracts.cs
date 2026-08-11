namespace RentRight.Features.Administration.Contracts;

public sealed record AdministrationUsersQuery(int Page = 1, int PageSize = 25)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 50;
}

public sealed record AdministrationUserRow(
    string UserId,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles);

public sealed record AdministrationUsersPage(
    IReadOnlyList<AdministrationUserRow> Users,
    IReadOnlyList<string> AvailableRoles,
    int Page,
    int PageSize,
    int TotalUsers)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalUsers / (double)PageSize));
}

public enum ListAdministrationUsersStatus
{
    Success,
    Unauthenticated,
    Forbidden
}

public sealed record ListAdministrationUsersOutcome(
    ListAdministrationUsersStatus Status,
    AdministrationUsersPage? Page = null)
{
    public static ListAdministrationUsersOutcome Success(AdministrationUsersPage page) =>
        new(ListAdministrationUsersStatus.Success, page);

    public static ListAdministrationUsersOutcome Unauthenticated() =>
        new(ListAdministrationUsersStatus.Unauthenticated);

    public static ListAdministrationUsersOutcome Forbidden() =>
        new(ListAdministrationUsersStatus.Forbidden);
}

public sealed record UpdateUserRolesCommand(
    string UserId,
    IReadOnlyCollection<string> SelectedRoles);

public enum UpdateUserRolesStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    UserNotFound,
    InvalidRoles,
    LastAdministrator,
    IdentityFailure,
    ConcurrentUpdate
}

public sealed record UpdateUserRolesOutcome(
    UpdateUserRolesStatus Status,
    string? UserEmail = null,
    IReadOnlyList<string>? Errors = null)
{
    public static UpdateUserRolesOutcome Success(string userEmail) =>
        new(UpdateUserRolesStatus.Success, userEmail);

    public static UpdateUserRolesOutcome Unauthenticated() =>
        new(UpdateUserRolesStatus.Unauthenticated);

    public static UpdateUserRolesOutcome Forbidden() =>
        new(UpdateUserRolesStatus.Forbidden);

    public static UpdateUserRolesOutcome UserNotFound() =>
        new(UpdateUserRolesStatus.UserNotFound);

    public static UpdateUserRolesOutcome InvalidRoles(IReadOnlyList<string> roles) =>
        new(UpdateUserRolesStatus.InvalidRoles, Errors: roles);

    public static UpdateUserRolesOutcome LastAdministrator() =>
        new(UpdateUserRolesStatus.LastAdministrator);

    public static UpdateUserRolesOutcome IdentityFailure(IReadOnlyList<string> errors) =>
        new(UpdateUserRolesStatus.IdentityFailure, Errors: errors);

    public static UpdateUserRolesOutcome ConcurrentUpdate() =>
        new(UpdateUserRolesStatus.ConcurrentUpdate);
}
