using RentRight.Models;

namespace RentRight.Features.Dashboards.Contracts;

public enum DashboardDestination
{
    Admin,
    Landlord,
    Tenant,
    Generic
}

public enum DashboardQueryStatus
{
    Success,
    Unauthenticated,
    Forbidden
}

public sealed record DashboardRouteOutcome(
    DashboardQueryStatus Status,
    DashboardDestination? Destination = null)
{
    public static DashboardRouteOutcome Success(DashboardDestination destination) =>
        new(DashboardQueryStatus.Success, destination);

    public static DashboardRouteOutcome Unauthenticated() =>
        new(DashboardQueryStatus.Unauthenticated);
}

public sealed record AdminDashboardSummary(int TotalUsers, int TotalProperties);

public sealed record AdminDashboardOutcome(
    DashboardQueryStatus Status,
    AdminDashboardSummary? Dashboard = null)
{
    public static AdminDashboardOutcome Success(AdminDashboardSummary dashboard) =>
        new(DashboardQueryStatus.Success, dashboard);

    public static AdminDashboardOutcome Unauthenticated() =>
        new(DashboardQueryStatus.Unauthenticated);

    public static AdminDashboardOutcome Forbidden() =>
        new(DashboardQueryStatus.Forbidden);
}

public sealed record LandlordPropertySummary(
    int Id,
    string Title,
    string Address,
    decimal RentAmount);

public sealed record LandlordDashboardSummary(
    IReadOnlyList<LandlordPropertySummary> Properties);

public sealed record LandlordDashboardOutcome(
    DashboardQueryStatus Status,
    LandlordDashboardSummary? Dashboard = null)
{
    public static LandlordDashboardOutcome Success(LandlordDashboardSummary dashboard) =>
        new(DashboardQueryStatus.Success, dashboard);

    public static LandlordDashboardOutcome Unauthenticated() =>
        new(DashboardQueryStatus.Unauthenticated);

    public static LandlordDashboardOutcome Forbidden() =>
        new(DashboardQueryStatus.Forbidden);
}

public sealed record TenantTenancySummary(
    int Id,
    string PropertyTitle,
    string PropertyAddress,
    DateOnly StartDate,
    DateOnly? EndDate,
    TenancyStatus Status,
    bool IsCurrentlyActive);

public sealed record TenantMaintenanceSummary(
    int Id,
    string PropertyTitle,
    DateTime DateSubmitted,
    MaintenanceRequestStatus Status);

public sealed record TenantMaintenanceCounts(int Open, int Recent);

public sealed record TenantDashboardSummary(
    IReadOnlyList<TenantTenancySummary> Tenancies,
    IReadOnlyList<TenantMaintenanceSummary> RecentMaintenanceRequests)
{
    public TenantMaintenanceCounts Counts { get; init; } = new(0, 0);
}

public sealed record TenantDashboardOutcome(
    DashboardQueryStatus Status,
    TenantDashboardSummary? Dashboard = null)
{
    public static TenantDashboardOutcome Success(TenantDashboardSummary dashboard) =>
        new(DashboardQueryStatus.Success, dashboard);

    public static TenantDashboardOutcome Unauthenticated() =>
        new(DashboardQueryStatus.Unauthenticated);

    public static TenantDashboardOutcome Forbidden() =>
        new(DashboardQueryStatus.Forbidden);
}
