namespace RentRight.Integrations.Geocoding;

public enum GeocodingStatus { Disabled, Found, NoResult, Unavailable, RateLimited, InvalidResponse }

public sealed record GeocodedLocation(decimal Latitude, decimal Longitude, string PlaceId)
{
    public const int MaximumPlaceIdLength = 2048;
}
public sealed record GeocodingResult(GeocodingStatus Status, GeocodedLocation? Location = null);

public interface IGeocodingClient
{
    bool IsEnabled { get; }
    Task<GeocodingResult> LookupAsync(string address, CancellationToken cancellationToken);
}

public sealed class GeocodingOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 3;
    public int DailyRequestLimit { get; set; } = 2500;
}
