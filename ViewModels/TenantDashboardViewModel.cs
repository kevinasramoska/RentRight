using RentRight.Models;

namespace RentRight.ViewModels;

public class TenantDashboardViewModel
{
    public int OpenRequestCount { get; init; }
    public int RecentRequestCount { get; init; }
    public IReadOnlyList<TenantTenancySummaryViewModel> Tenancies { get; init; } = [];
    public IReadOnlyList<TenantDashboardMaintenanceRequestViewModel> RecentMaintenanceRequests
        { get; init; } = [];
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

public class TenantDashboardMaintenanceRequestViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public DateTime DateSubmitted { get; init; }
    public MaintenanceRequestStatus Status { get; init; }
}
