using System.Net;
using RentRight.Health;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class HealthSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString) { EnvironmentName = "Production" };

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(HealthEndpoints.LivenessPath)]
    [InlineData(HealthEndpoints.ReadinessPath)]
    public async Task Health_MigratedSqlServer_ReportsHealthyAnonymously(string path)
    {
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
