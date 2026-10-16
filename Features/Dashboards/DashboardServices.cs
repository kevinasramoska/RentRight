using RentRight.Application.Abstractions;
using RentRight.Features.Dashboards.Contracts;

namespace RentRight.Features.Dashboards;

public sealed class DashboardRouteService(ICurrentUser currentUser) : IDashboardRouteService
{
    public DashboardRouteOutcome Resolve()
    {
        if (!currentUser.IsAuthenticated)
        {
            return DashboardRouteOutcome.Unauthenticated();
        }

        if (currentUser.IsInRole("Admin"))
        {
            return DashboardRouteOutcome.Success(DashboardDestination.Admin);
        }

        if (currentUser.IsInRole("Landlord"))
        {
            return DashboardRouteOutcome.Success(DashboardDestination.Landlord);
        }

        return currentUser.IsInRole("Tenant")
            ? DashboardRouteOutcome.Success(DashboardDestination.Tenant)
            : DashboardRouteOutcome.Success(DashboardDestination.Generic);
    }
}

public sealed class AdminDashboardQueryService(
    IDashboardDataStore dataStore,
    ICurrentUser currentUser) : IAdminDashboardQueryService
{
    public async Task<AdminDashboardOutcome> QueryAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return AdminDashboardOutcome.Unauthenticated();
        }

        if (!currentUser.IsInRole("Admin"))
        {
            return AdminDashboardOutcome.Forbidden();
        }

        var dashboard = await dataStore.GetAdminSummaryAsync(cancellationToken);
        return AdminDashboardOutcome.Success(dashboard);
    }
}

public sealed class LandlordDashboardQueryService(
    IDashboardDataStore dataStore,
    ICurrentUser currentUser) : ILandlordDashboardQueryService
{
    private const int MaximumProperties = 100;

    public async Task<LandlordDashboardOutcome> QueryAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return LandlordDashboardOutcome.Unauthenticated();
        }

        if (!currentUser.IsInRole("Landlord"))
        {
            return LandlordDashboardOutcome.Forbidden();
        }

        var properties = await dataStore.ListLandlordPropertiesAsync(
            currentUser.UserId,
            MaximumProperties,
            cancellationToken);
        return LandlordDashboardOutcome.Success(new LandlordDashboardSummary(properties));
    }
}

public sealed class TenantDashboardQueryService(
    IDashboardDataStore dataStore,
    ICurrentUser currentUser,
    IClock clock) : ITenantDashboardQueryService
{
    private const int MaximumTenancies = 100;
    private const int MaximumRecentMaintenanceRequests = 5;

    public async Task<TenantDashboardOutcome> QueryAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return TenantDashboardOutcome.Unauthenticated();
        }

        if (!currentUser.IsInRole("Tenant"))
        {
            return TenantDashboardOutcome.Forbidden();
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var tenancies = await dataStore.ListTenantTenanciesAsync(
            currentUser.UserId,
            today,
            MaximumTenancies,
            cancellationToken);
        var maintenanceRequests = await dataStore.ListTenantMaintenanceAsync(
            currentUser.UserId,
            today,
            MaximumRecentMaintenanceRequests,
            cancellationToken);

        var counts = await dataStore.GetTenantMaintenanceCountsAsync(currentUser.UserId, today, now.AddDays(-30), cancellationToken);
        return TenantDashboardOutcome.Success(
            new TenantDashboardSummary(tenancies, maintenanceRequests) { Counts = counts });
    }
}
