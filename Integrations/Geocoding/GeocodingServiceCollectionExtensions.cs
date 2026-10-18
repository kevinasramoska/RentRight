using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RentRight.Integrations.Geocoding;

public static class GeocodingServiceCollectionExtensions
{
    public static IServiceCollection AddGeocoding(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<GeocodingOptions>().Bind(configuration.GetSection("Geocoding"))
            .Validate(options => !options.Enabled || (!string.IsNullOrWhiteSpace(options.ApiKey) &&
                options.ApiKey.Length <= 256 && options.TimeoutSeconds is >= 1 and <= 10 &&
                options.DailyRequestLimit is >= 1 and <= 3000),
                "Enabled geocoding requires an API key, a 1–10 second timeout and a 1–3000 daily request limit.").ValidateOnStart();
        services.AddSingleton<GeocodingState>();
        services.AddHttpClient<IGeocodingClient, GeoapifyGeocodingClient>(http =>
        {
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false
        }).RemoveAllLoggers(); // The provider requires address/API key query parameters.
        return services;
    }
}
