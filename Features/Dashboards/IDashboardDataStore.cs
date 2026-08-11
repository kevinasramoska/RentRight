using RentRight.Features.Dashboards.Contracts;

namespace RentRight.Features.Dashboards;

public interface IDashboardDataStore
{
    Task<AdminDashboardSummary> GetAdminSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LandlordPropertySummary>> ListLandlordPropertiesAsync(
        string landlordId,
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantTenancySummary>> ListTenantTenanciesAsync(
        string tenantId,
        DateOnly today,
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantMaintenanceSummary>> ListTenantMaintenanceAsync(
        string tenantId,
        int maximumResults,
        CancellationToken cancellationToken);
}
