using System.Diagnostics;

namespace RentRight.Diagnostics;

public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, RentalDiagnostics diagnostics)
    {
        // ASP.NET normally owns the server activity. Test hosts and collector-free
        // pipelines may omit it or supply a legacy activity; still provide W3C
        // context for SQL/HttpClient/errors without logging legacy parent values.
        using var fallback = Activity.Current?.IdFormat != ActivityIdFormat.W3C ? CreateFallback(context) : null;
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        var status = 0;
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
            status = 499; // Diagnostic classification only; do not write a new response.
            throw;
        }
        catch (Exception)
        {
            outcome = context.Response.HasStarted ? "aborted" : "failed";
            status = context.Response.HasStarted ? context.Response.StatusCode : 500;
            DiagnosticEvents.RecordFailure(logger, context);
            throw;
        }
        finally
        {
            if (status == 0) status = context.Response.StatusCode;
            if (status == 499) outcome = "cancelled";
            else if (status >= 500 && outcome == "completed") outcome = "failed";
            diagnostics.RecordRequest(context, Stopwatch.GetElapsedTime(started), outcome, status);
        }
    }

    private static Activity CreateFallback(HttpContext context)
    {
        var activity = new Activity("RentRight.HttpRequest").SetIdFormat(ActivityIdFormat.W3C);
        if (ActivityContext.TryParse(context.Request.Headers["traceparent"], traceState: null, out var parent))
            activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
        return activity.Start();
    }
}
