using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace RentRight.Diagnostics;

public sealed class RentalDiagnostics : IDisposable
{
    public const string Name = "RentRight";
    private readonly ActivitySource _activities = new(Name);
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _databaseOperations;
    private readonly Histogram<double> _databaseDuration;
    private readonly DiagnosticsOptions _options;
    private readonly ILogger<RentalDiagnostics> _logger;

    public RentalDiagnostics(IMeterFactory meters, IOptions<DiagnosticsOptions> options, ILogger<RentalDiagnostics> logger)
    {
        _options = options.Value;
        _logger = logger;
        var meter = meters.Create(Name);
        _requests = meter.CreateCounter<long>("rentright.http.requests", unit: "{request}");
        _requestDuration = meter.CreateHistogram<double>("rentright.http.duration", unit: "s");
        _databaseOperations = meter.CreateCounter<long>("rentright.database.operations", unit: "{operation}");
        _databaseDuration = meter.CreateHistogram<double>("rentright.database.duration", unit: "s");
    }

    public bool Enabled => _options.Enabled;

    internal void RecordRequest(HttpContext context, TimeSpan elapsed, string outcome, int statusCode)
    {
        if (!Enabled) return;
        var method = DiagnosticEvents.Method(context);
        var route = DiagnosticEvents.Route(context);
        // Never use raw paths, queries, user IDs, IPs or trace IDs as metric dimensions.
        var tags = new TagList
        {
            { "http.request.method", method }, { "http.route", route },
            { "http.response.status_code", statusCode }, { "outcome", outcome }
        };
        _requests.Add(1, tags);
        _requestDuration.Record(elapsed.TotalSeconds, tags);
        var rate = _options.SuccessfulRequestLogSampleRate;
        if (statusCode >= 400 || outcome != "completed" || rate >= 1 || rate > 0 && Random.Shared.NextDouble() < rate)
            DiagnosticEvents.RequestCompleted(_logger, statusCode >= 400 || outcome != "completed" ? LogLevel.Warning : LogLevel.Information,
                method, route, statusCode, Math.Round(elapsed.TotalMilliseconds, 3), outcome,
                DiagnosticEvents.TraceId(context), Activity.Current?.SpanId.ToHexString() ?? string.Empty);
    }

    internal Activity? StartGeocoding() => Enabled
        ? _activities.StartActivity("geocoding.lookup", ActivityKind.Client) : null;

    internal void RecordDatabase(string operation, DateTimeOffset start, TimeSpan duration, string outcome)
    {
        if (!Enabled) return;
        var tags = new TagList { { "db.system.name", "microsoft.sql_server" }, { "operation", operation }, { "outcome", outcome } };
        _databaseOperations.Add(1, tags);
        _databaseDuration.Record(duration.TotalSeconds, tags);
        // Record completed spans from EF's measured interval, without holding commands,
        // readers, exceptions or AsyncLocal state across asynchronous interceptor calls.
        using var activity = _activities.StartActivity("database." + operation, ActivityKind.Client,
            Activity.Current?.Context ?? default, tags: tags, startTime: start);
        if (activity is not null)
        {
            if (outcome == "failed") activity.SetStatus(ActivityStatusCode.Error);
            activity.SetEndTime((start + duration).UtcDateTime);
        }
        if (outcome == "failed") DiagnosticEvents.DatabaseFailure(_logger, operation,
            Math.Round(duration.TotalMilliseconds, 3), Activity.Current?.TraceId.ToHexString() ?? string.Empty);
    }

    public void Dispose() => _activities.Dispose();
}
