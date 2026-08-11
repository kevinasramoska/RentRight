namespace RentRight.Features.Properties;

public static class PropertiesServiceCollectionExtensions
{
    public static IServiceCollection AddPropertiesFeature(this IServiceCollection services)
    {
        services.AddScoped<IPropertyStore, EfPropertyStore>();
        services.AddScoped<IPropertyService, PropertyService>();

        return services;
    }
}
