using RentRight.Features.Dashboards.Contracts;

namespace RentRight.Features.Dashboards;

public interface IDashboardRouteService
{
    DashboardRouteOutcome Resolve();
}

public interface IAdminDashboardQueryService
{
    Task<AdminDashboardOutcome> QueryAsync(CancellationToken cancellationToken);
}

public interface ILandlordDashboardQueryService
{
    Task<LandlordDashboardOutcome> QueryAsync(CancellationToken cancellationToken);
}

public interface ITenantDashboardQueryService
{
    Task<TenantDashboardOutcome> QueryAsync(CancellationToken cancellationToken);
}
