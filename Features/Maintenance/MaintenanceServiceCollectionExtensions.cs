namespace RentRight.Features.Maintenance;

public static class MaintenanceServiceCollectionExtensions
{
    public static IServiceCollection AddMaintenanceFeature(this IServiceCollection services)
    {
        services.AddScoped<ILandlordMaintenanceService, LandlordMaintenanceService>();
        services.AddScoped<ITenantMaintenanceService, TenantMaintenanceService>();
        services.AddScoped<IMaintenanceStore, EfMaintenanceStore>();
        services.AddScoped<IMaintenanceQueryService, MaintenanceQueryService>();
        services.AddScoped<IMaintenanceCommandService, MaintenanceCommandService>();

        return services;
    }
}
