using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RentRight.Health;

public static class HealthEndpoints
{
    public static IServiceCollection AddRentalHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<SqlReadinessHealthCheck>("sql", tags: ["ready"],
            timeout: SqlReadinessHealthCheck.Timeout);
        return services;
    }

    public static void MapRentalHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", Options(_ => false))
            .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]))
            .AllowAnonymous()
            // Cookie validation can query SQL. Liveness must bypass it, even if
            // a caller accidentally sends a session cookie during a SQL outage.
            .ShortCircuit();
        app.MapHealthChecks("/health/ready", Options(check => check.Tags.Contains("ready")))
            .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]))
            .AllowAnonymous()
            .RequireRateLimiting(new ReadinessConcurrencyPolicy());
    }

    private static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        AllowCachingResponses = false,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        },
        ResponseWriter = (context, report) => WriteStatus(context, report.Status == HealthStatus.Healthy)
    };

    private static Task WriteStatus(HttpContext context, bool healthy)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return HttpMethods.IsHead(context.Request.Method) ? Task.CompletedTask :
            context.Response.WriteAsync(healthy ? "Healthy" : "Unhealthy", context.RequestAborted);
    }

    private sealed class ReadinessConcurrencyPolicy : IRateLimiterPolicy<string>
    {
        public RateLimitPartition<string> GetPartition(HttpContext context) =>
            RateLimitPartition.GetConcurrencyLimiter("readiness", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2, QueueLimit = 0
            });

        public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected => (rejected, _) =>
        {
            rejected.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return new ValueTask(WriteStatus(rejected.HttpContext, healthy: false));
        };
    }
}
