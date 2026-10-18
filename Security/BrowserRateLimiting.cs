using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RentRight.Api;

namespace RentRight.Security;

public sealed class BrowserRateLimitOptions
{
    public int AuthenticationPermitLimit { get; set; } = 20;
    public int MutationPermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}

public static class BrowserRateLimiting
{
    public static IServiceCollection AddBrowserRateLimiting(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BrowserRateLimitOptions>()
            .Bind(configuration.GetSection("Security:RateLimits"))
            .Validate(options => options.AuthenticationPermitLimit is >= 1 and <= 10000 &&
                options.MutationPermitLimit is >= 1 and <= 10000 && options.WindowSeconds is >= 1 and <= 3600,
                "Rate limits must be positive, bounded permit counts and a 1–3600 second window.")
            .ValidateOnStart();
        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<BrowserRateLimitOptions>>((options, settings) =>
        {
            var limits = settings.Value;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
                    HttpMethods.IsOptions(context.Request.Method))
                    return RateLimitPartition.GetNoLimiter("reads");

                var authentication = context.Request.Path.StartsWithSegments("/Identity/Account");
                // Never partition by submitted login names, arbitrary URLs or forwarded headers.
                var ip = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
                var userId = context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
                var partition = authentication ? "authentication:ip:" + ip :
                    !string.IsNullOrWhiteSpace(userId) ? "mutation:user:" + userId : "mutation:ip:" + ip;
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authentication ? limits.AuthenticationPermitLimit : limits.MutationPermitLimit,
                    Window = TimeSpan.FromSeconds(limits.WindowSeconds), QueueLimit = 0, AutoReplenishment = true
                });
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var context = rejected.HttpContext;
                var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : limits.WindowSeconds;
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";
                const string message = "Too many requests. Please wait before trying again.";
                if (context.Request.Path.StartsWithSegments('/' + ApiConventions.RoutePrefix))
                {
                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests, Title = message
                    };
                    ApiProblemDetailsDefaults.Apply(problem, context);
                    await context.Response.WriteAsJsonAsync(problem, options: null,
                        contentType: "application/problem+json", cancellationToken: cancellationToken);
                }
                else
                {
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync(message, cancellationToken);
                }
            };
        });
        return services;
    }
}
