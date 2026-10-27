using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RentRight.Health;

// Liveness reports only that the process serves HTTP; readiness adds a bounded SQL check.
// Responses carry the overall status word only: no check names, timings, exceptions or configuration.
public static class HealthEndpoints
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";
    private const string ReadinessTag = "ready";
    private static readonly TimeSpan SqlTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddRentalHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SqlReadinessCheck>("sql", HealthStatus.Unhealthy, [ReadinessTag], SqlTimeout);
        return services;
    }

    public static IEndpointRouteBuilder MapRentalHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, CreateOptions(_ => false));
        endpoints.MapHealthChecks(ReadinessPath, CreateOptions(registration => registration.Tags.Contains(ReadinessTag)));
        return endpoints;
    }

    private static HealthCheckOptions CreateOptions(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        AllowCachingResponses = false,
        ResponseWriter = (context, report) =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(report.Status.ToString(), context.RequestAborted);
        }
    };
}
