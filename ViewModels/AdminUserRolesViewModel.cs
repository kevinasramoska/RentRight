namespace RentRight.ViewModels;

public class AdminUserRolesViewModel
{
    public IReadOnlyList<UserRoleRowViewModel> Users { get; init; } = [];
    public IReadOnlyList<string> AvailableRoles { get; init; } = [];
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalUsers { get; init; }
}

public class UserRoleRowViewModel
{
    public required string UserId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
