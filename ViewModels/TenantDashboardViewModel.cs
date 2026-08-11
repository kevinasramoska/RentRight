using RentRight.Models;

namespace RentRight.ViewModels;

public class TenantDashboardViewModel
{
    public IReadOnlyList<TenantTenancySummaryViewModel> Tenancies { get; init; } = [];
    public IReadOnlyList<MaintenanceRequestRowViewModel> RecentMaintenanceRequests { get; init; } = [];
}

public class TenantTenancySummaryViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public required string PropertyAddress { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public TenancyStatus Status { get; init; }
    public bool IsCurrentlyActive { get; init; }
}
