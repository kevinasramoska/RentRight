using Microsoft.AspNetCore.Antiforgery;

namespace RentRight.Security;

public static class BrowserSecurity
{
    // Deliberately a baseline, not a claim of a full script-src policy: the
    // framework Identity UI and existing Razor layout still use inline scripts.
    public const string ContentSecurityPolicy =
        "base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";

    public static IServiceCollection AddBrowserSecurity(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<AntiforgeryOptions>(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddBrowserRateLimiting(configuration);
        services.AddPersistentDataProtection(configuration, environment);
        return services;
    }

    public static IApplicationBuilder UseBrowserSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // OnStarting also covers static files, redirects and handled errors.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                return Task.CompletedTask;
            });
            await next(context);
        });
}
