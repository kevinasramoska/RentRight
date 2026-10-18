using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RentRight.Integrations.Geocoding;

public sealed class GeoapifyGeocodingClient(HttpClient http, IOptions<GeocodingOptions> settings,
    GeocodingState state, TimeProvider clock) : IGeocodingClient
{
    private static readonly Meter Meter = new("RentRight.Geocoding");
    private static readonly Counter<long> Lookups = Meter.CreateCounter<long>("rentright.geocoding.lookups");
    public bool IsEnabled => settings.Value.Enabled;

    public async Task<GeocodingResult> LookupAsync(string address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled) return Report(new(GeocodingStatus.Disabled));
        string normalized;
        try { normalized = string.Join(' ', address.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); }
        catch (ArgumentException) { return Report(new(GeocodingStatus.InvalidResponse)); }
        if (normalized.Length is < 1 or > 500) return Report(new(GeocodingStatus.NoResult));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
        if (state.Get(key, clock.GetUtcNow()) is { } cached) return Report(cached, cached: true);
        if (!await state.Gate.WaitAsync(0, cancellationToken)) return Report(new(GeocodingStatus.RateLimited));
        try
        {
            if (state.Get(key, clock.GetUtcNow()) is { } rechecked) return Report(rechecked, cached: true);
            if (clock.GetUtcNow() < state.NextAllowedUtc) return Report(new(GeocodingStatus.RateLimited));
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds), clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    var wait = state.NextAllowedUtc - clock.GetUtcNow();
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, linked.Token);
                }
                state.NextAllowedUtc = clock.GetUtcNow().AddSeconds(1);
                if (!state.TryTakeRequest(clock.GetUtcNow(), settings.Value.DailyRequestLimit))
                    return Report(new(GeocodingStatus.RateLimited));
                try
                {
                    // Fixed origin/path; no browser-supplied URL, automatic redirect or cookies.
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        "https://api.geoapify.com/v1/geocode/search?format=json&limit=1&bias=countrycode:none&text=" +
                        Uri.EscapeDataString(normalized) + "&apiKey=" + Uri.EscapeDataString(settings.Value.ApiKey!));
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                        ((int)response.StatusCode >= 500 && response.Headers.RetryAfter is not null))
                    {
                        var retry = response.Headers.RetryAfter;
                        var seconds = retry?.Delta?.TotalSeconds ??
                            (retry?.Date - clock.GetUtcNow())?.TotalSeconds ?? 60;
                        state.NextAllowedUtc = seconds > 86400 ? DateTimeOffset.MaxValue :
                            clock.GetUtcNow().AddSeconds(Math.Max(1, seconds));
                        return Report(new(GeocodingStatus.RateLimited));
                    }
                    if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                    {
                        if (attempt == 0) continue;
                        return Report(new(GeocodingStatus.Unavailable));
                    }
                    if (!response.IsSuccessStatusCode) return Report(new(GeocodingStatus.Unavailable));
                    var result = await ParseAsync(response, linked.Token);
                    state.Put(key, result, clock.GetUtcNow());
                    return Report(result);
                }
                catch (HttpRequestException)
                {
                    if (attempt == 1) return Report(new(GeocodingStatus.Unavailable));
                }
            }
            return Report(new(GeocodingStatus.Unavailable));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(new(GeocodingStatus.Unavailable));
        }
        catch (IOException) { return Report(new(GeocodingStatus.Unavailable)); }
        finally { state.Gate.Release(); }
    }

    private static async Task<GeocodingResult> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maximumBytes = 65536;
        if (response.Content.Headers.ContentLength > maximumBytes) return new(GeocodingStatus.InvalidResponse);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > maximumBytes) return new(GeocodingStatus.InvalidResponse);
            buffer.Write(chunk, 0, count);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array) return new(GeocodingStatus.InvalidResponse);
            if (results.GetArrayLength() == 0) return new(GeocodingStatus.NoResult);
            var item = results[0];
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("lat", out var latitude) ||
                !item.TryGetProperty("lon", out var longitude) || latitude.ValueKind != JsonValueKind.Number ||
                longitude.ValueKind != JsonValueKind.Number || !latitude.TryGetDecimal(out var lat) ||
                !longitude.TryGetDecimal(out var lon) || lat is < -90 or > 90 || lon is < -180 or > 180 ||
                !item.TryGetProperty("place_id", out var place) || place.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(place.GetString()) || place.GetString()!.Length > GeocodedLocation.MaximumPlaceIdLength)
                return new(GeocodingStatus.InvalidResponse);
            // Do not display a city/country centroid or a low-confidence guess as a home.
            // Limit this adapter to OSM-backed results so static source attribution is correct.
            if (!item.TryGetProperty("result_type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() is not ("building" or "amenity" or "street") ||
                !item.TryGetProperty("datasource", out var source) || source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("sourcename", out var name) || name.ValueKind != JsonValueKind.String ||
                name.GetString() != "openstreetmap" || !item.TryGetProperty("rank", out var rank) ||
                rank.ValueKind != JsonValueKind.Object || !rank.TryGetProperty("confidence", out var confidence) ||
                confidence.ValueKind != JsonValueKind.Number || !confidence.TryGetDecimal(out var score) || score is < 0.8m or > 1)
                return new(GeocodingStatus.NoResult);
            return new(GeocodingStatus.Found, new(decimal.Round(lat, 6), decimal.Round(lon, 6), place.GetString()!));
        }
        catch (JsonException) { return new(GeocodingStatus.InvalidResponse); }
    }

    private static GeocodingResult Report(GeocodingResult result, bool cached = false)
    {
        Lookups.Add(1, new KeyValuePair<string, object?>("outcome", result.Status.ToString()),
            new KeyValuePair<string, object?>("cached", cached));
        return result;
    }
}
