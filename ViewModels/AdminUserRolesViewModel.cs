using RentRight.Models;

namespace RentRight.ViewModels;

public class AdminUserRolesViewModel
{
    public IReadOnlyList<UserRoleRowViewModel> Users { get; init; } = [];
    public IReadOnlyList<string> AvailableRoles { get; init; } = [];
}

public class UserRoleRowViewModel
{
    public required ApplicationUser User { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
