using System.Diagnostics;
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Integrations.Geocoding;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class HealthSqlTests : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly UnavailableGeocoding _geocoding = new();
    private readonly SqlApplication _app;

    public HealthSqlTests(SqlServerFixture server)
    {
        _app = new SqlApplication(server.ConnectionString)
        {
            ConfigureTestServices = services =>
            {
                services.Configure<GeocodingOptions>(options => { options.Enabled = true; options.ApiKey = "synthetic-unused-key"; });
                services.AddSingleton<IGeocodingClient>(_geocoding);
            }
        };
    }

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Fact]
    public async Task Probes_RealSqlAndUnavailableOptionalProvider_AreHealthyWithoutProviderCallsOrWrites()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        Assert.Equal(0, _geocoding.Calls);
        await using var database = _app.CreateContext();
        Assert.Empty(await database.Properties.ToListAsync());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.Equal(3, await database.Roles.CountAsync());
    }

    [Fact]
    public async Task Readiness_OwnedDatabaseOfflineThenOnline_FailsSafelyAndRecoversWhileLivenessStaysHealthy()
    {
        using var anonymous = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var signedIn = await _app.CreateActorClientAsync(Actor.Admin);
        try
        {
            await SetDatabaseOnline(false);
            var started = Stopwatch.GetTimestamp();
            using var unavailable = await anonymous.GetAsync("/health/ready").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("Unhealthy", await unavailable.Content.ReadAsStringAsync());
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
            Assert.True(unavailable.Headers.CacheControl?.NoStore);
            // A real persisted login cookie must not force liveness through Identity's SQL validation.
            using var live = await signedIn.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
            Assert.DoesNotContain(_app.Logs, message => message.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Password=", StringComparison.OrdinalIgnoreCase));
        }
        finally { await SetDatabaseOnline(true); }

        using var recovered = await anonymous.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("Healthy", await recovered.Content.ReadAsStringAsync());
        using var properties = await anonymous.GetAsync("/api/v1/properties");
        Assert.Equal(HttpStatusCode.OK, properties.StatusCode);
        Assert.Equal(0, _geocoding.Calls);
    }

    private async Task SetDatabaseOnline(bool online)
    {
        await using var database = _app.CreateContext();
        var connectionString = new SqlConnectionStringBuilder(database.Database.GetConnectionString());
        var target = connectionString.InitialCatalog;
        Assert.Matches("^RentRightTests_[a-f0-9]{32}$", target); // Only this test's random, disposable database.
        connectionString.InitialCatalog = "master";
        await using var connection = new SqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = $"ALTER DATABASE [{target}] SET {(online ? "ONLINE" : "OFFLINE WITH ROLLBACK IMMEDIATE")}";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class UnavailableGeocoding : IGeocodingClient
    {
        public int Calls { get; private set; }
        public bool IsEnabled => true;
        public Task<GeocodingResult> LookupAsync(string address, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Health probes must not call the optional provider.");
        }
    }
}
