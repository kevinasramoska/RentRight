namespace RentRight.Features.Properties;

public static class PropertiesServiceCollectionExtensions
{
    public static IServiceCollection AddPropertiesFeature(this IServiceCollection services)
    {
        services.AddScoped<IPropertyStore, EfPropertyStore>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IPropertyLocationWriter, EfPropertyLocationWriter>();
        services.AddScoped<IPropertyLocationService, PropertyLocationService>();

        return services;
    }
}
