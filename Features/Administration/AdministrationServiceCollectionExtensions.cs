namespace RentRight.Features.Administration;

public static class AdministrationServiceCollectionExtensions
{
    public static IServiceCollection AddAdministrationFeature(this IServiceCollection services)
    {
        services.AddScoped<IAdministrationRoleStore, IdentityAdministrationRoleStore>();
        services.AddScoped<IAdministrationService, AdministrationService>();

        return services;
    }
}
