using RentRight.Features.Dashboards;

namespace RentRight.Features.Tenancies;

public static class TenanciesServiceCollectionExtensions
{
    public static IServiceCollection AddTenanciesAndDashboardsFeature(
        this IServiceCollection services)
    {
        services.AddScoped<ITenancyStore, EfTenancyStore>();
        services.AddScoped<ITenancyService, TenancyService>();

        services.AddScoped<IDashboardDataStore, EfDashboardDataStore>();
        services.AddScoped<IDashboardRouteService, DashboardRouteService>();
        services.AddScoped<IAdminDashboardQueryService, AdminDashboardQueryService>();
        services.AddScoped<ILandlordDashboardQueryService, LandlordDashboardQueryService>();
        services.AddScoped<ITenantDashboardQueryService, TenantDashboardQueryService>();

        return services;
    }
}
