using System.Reflection;
using Microsoft.Extensions.Options;

namespace RentRight.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddRentalDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DiagnosticsOptions>().Bind(configuration.GetSection("Diagnostics"))
            .Validate(options => double.IsFinite(options.SuccessfulRequestLogSampleRate) &&
                options.SuccessfulRequestLogSampleRate is >= 0 and <= 1,
                "Diagnostics successful-request sample rate must be a finite number between 0 and 1.")
            .ValidateOnStart();
        services.AddMetrics();
        services.AddSingleton<RentalDiagnostics>();
        services.AddSingleton<SafeDatabaseCommandInterceptor>();
        services.AddSingleton<SafeDatabaseConnectionInterceptor>();
        services.AddExceptionHandler<SafeExceptionHandler>();
        services.AddLogging(logging =>
        {
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = false; // Arbitrary framework/application scopes may contain personal data.
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";
            });
            // These categories can copy raw URLs, SQL/provider errors or exception payloads.
            // The request/failure events and EF interceptors replace them with allow-listed fields.
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.None);
            logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
        });
        return services;
    }

    public static WebApplication UseRentalDiagnostics(this WebApplication app)
    {
        app.UseMiddleware<RequestDiagnosticsMiddleware>();
        app.Lifetime.ApplicationStarted.Register(() => DiagnosticEvents.Started(
            app.Services.GetRequiredService<ILogger<RentalDiagnostics>>(),
            app.Services.GetRequiredService<IOptions<DiagnosticsOptions>>().Value.Enabled,
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"));
        return app;
    }
}
