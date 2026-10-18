using System.Net;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RentRight.Api;
using RentRight.Data;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

public enum Actor { Anonymous, Tenant, OwningLandlord, OtherLandlord, MultiRoleAdmin, Admin }

// Each test owns an empty, randomly named database. Only the connection is replaced;
// production Identity, authorization, filters, services and stores stay registered.
public sealed class SqlApplication : WebApplicationFactory<Program>
{
    public ConcurrentQueue<string> Logs { get; } = new();
    private readonly string _connectionString;
    private readonly IInterceptor[] _interceptors;
    private readonly string _password = $"T!{Guid.NewGuid():N}a9";
    public string EnvironmentName { get; init; } = "Testing";
    public TimeProvider? Clock { get; init; }
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    public SqlApplication(string serverConnectionString, params IInterceptor[] interceptors)
    {
        _interceptors = interceptors;
        _connectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"RentRightTests_{Guid.NewGuid():N}",
            Pooling = false
        }.ConnectionString;
    }

    public ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_connectionString).AddInterceptors(_interceptors).Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("SeedAdmin:Email", string.Empty);
        builder.UseSetting("SeedAdmin:Password", string.Empty);
        builder.UseSetting("Geocoding:Enabled", "false"); // Enabled adapter tests explicitly install a fake transport.
        builder.ConfigureServices(services =>
        {
            services.AddLogging(logging => logging.AddProvider(new CaptureLogs(Logs)));
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString).AddInterceptors(_interceptors));
            if (Clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Clock);
            }
            ConfigureTestServices?.Invoke(services);
        });
    }

    public async Task InitializeAsync(string? targetMigration = null)
    {
        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
        }

        // Starts the real host after migrations, allowing its normal role seeding.
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var actor in Enum.GetValues<Actor>().Where(actor => actor != Actor.Anonymous))
        {
            var user = new ApplicationUser
            {
                Id = actor.ToString(), UserName = Email(actor), Email = Email(actor),
                EmailConfirmed = true, FullName = actor.ToString()
            };
            Assert.True((await users.CreateAsync(user, _password)).Succeeded);
            string[] roles = actor switch
            {
                Actor.Tenant => ["Tenant"],
                Actor.MultiRoleAdmin => ["Admin", "Landlord", "Tenant"],
                Actor.Admin => ["Admin"],
                _ => ["Landlord"]
            };
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }
    }

    public async Task<HttpClient> CreateActorClientAsync(Actor actor)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        if (actor == Actor.Anonymous) return client;

        try
        {
            using var page = await client.GetAsync("/Identity/Account/Login");
            page.EnsureSuccessStatusCode();
            var token = ExtractToken(await page.Content.ReadAsStringAsync());
            using var login = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Input.Email"] = Email(actor), ["Input.Password"] = _password,
                    ["__RequestVerificationToken"] = token
                }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Identity.Application="));
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async Task AddAntiforgeryAsync(HttpClient client)
    {
        using var page = await client.GetAsync("/Identity/Account/Manage");
        page.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove(ApiConventions.AntiforgeryHeaderName);
        client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName,
            ExtractToken(await page.Content.ReadAsStringAsync()));
    }

    private static string ExtractToken(string html)
    {
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        return token;
    }

    private static string Email(Actor actor) => $"{actor}@example.test";

    private sealed class CaptureLogs(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }
        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, error));
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
