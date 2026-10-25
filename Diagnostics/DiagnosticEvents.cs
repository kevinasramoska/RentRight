using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace RentRight.Diagnostics;

internal static partial class DiagnosticEvents
{
    private static readonly object FailureRecorded = new();

    public static string Route(HttpContext context) =>
        ((context.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? context.GetEndpoint())
            as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";

    public static string Method(HttpContext context) => context.Request.Method switch
    {
        "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" => context.Request.Method,
        _ => "OTHER"
    };

    public static string TraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;

    public static void RecordFailure(ILogger logger, HttpContext context)
    {
        if (context.Items.ContainsKey(FailureRecorded)) return;
        context.Items[FailureRecorded] = true;
        UnexpectedFailure(logger, Method(context), Route(context), TraceId(context));
    }

    [LoggerMessage(EventId = 13001, EventName = "HttpRequestCompleted",
        Message = "HTTP {Method} {Route} completed with {StatusCode} in {ElapsedMilliseconds} ms; outcome {Outcome}; trace {TraceId}; span {SpanId}.")]
    public static partial void RequestCompleted(ILogger logger, LogLevel logLevel,
        string method, string route, int statusCode, double elapsedMilliseconds, string outcome, string traceId, string spanId);

    [LoggerMessage(EventId = 13002, EventName = "UnexpectedRequestFailure", Level = LogLevel.Error,
        Message = "Unexpected HTTP failure for {Method} {Route}; trace {TraceId}. Exception payload intentionally omitted.")]
    private static partial void UnexpectedFailure(ILogger logger, string method, string route, string traceId);

    [LoggerMessage(EventId = 13003, EventName = "DatabaseOperationFailed", Level = LogLevel.Warning,
        Message = "Database {Operation} failed in {ElapsedMilliseconds} ms; trace {TraceId}. Provider payload intentionally omitted.")]
    public static partial void DatabaseFailure(ILogger logger, string operation, double elapsedMilliseconds, string traceId);

    [LoggerMessage(EventId = 13000, EventName = "DiagnosticsStarted", Level = LogLevel.Information,
        Message = "RentRight diagnostics enabled: {Enabled}; version {Version}. Console only; no remote exporter configured.")]
    public static partial void Started(ILogger logger, bool enabled, string version);
}
