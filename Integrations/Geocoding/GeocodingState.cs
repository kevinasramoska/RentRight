namespace RentRight.Integrations.Geocoding;

// One bounded, process-local cache/gate shared by the factory-created HTTP clients.
// Keys are address hashes, never raw address strings. No unbounded request queue.
public sealed class GeocodingState : IDisposable
{
    private readonly Dictionary<string, (DateTimeOffset Expires, GeocodingResult Result)> _cache = [];
    private readonly object _sync = new();
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTimeOffset NextAllowedUtc { get; set; }
    private DateOnly _day;
    private int _attempts;

    // Called only while holding Gate; retries count toward the UTC daily budget too.
    public bool TryTakeRequest(DateTimeOffset now, int limit)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        if (_day != day) { _day = day; _attempts = 0; }
        if (_attempts >= limit) return false;
        _attempts++;
        return true;
    }

    public GeocodingResult? Get(string key, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!_cache.TryGetValue(key, out var entry)) return null;
            if (entry.Expires > now) return entry.Result;
            _cache.Remove(key);
            return null;
        }
    }

    public void Put(string key, GeocodingResult result, DateTimeOffset now)
    {
        if (result.Status is not (GeocodingStatus.Found or GeocodingStatus.NoResult)) return;
        lock (_sync)
        {
            if (_cache.Count >= 256)
                _cache.Remove(_cache.MinBy(entry => entry.Value.Expires).Key);
            _cache[key] = (now.Add(result.Status == GeocodingStatus.Found ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(5)), result);
        }
    }

    public void Dispose() => Gate.Dispose();
}
