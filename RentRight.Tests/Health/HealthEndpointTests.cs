using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RentRight.Health;
using RentRight.Tests.Api;
using Xunit;

namespace RentRight.Tests.Health;

public class HealthEndpointTests
{
    [Fact]
    public void Registration_SqlReadiness_IsTaggedBoundedAndUnhealthyOnFailure()
    {
        using var factory = new RentRightApplicationFactory("Production");
        var registration = Assert.Single(
            factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        Assert.Equal("sql", registration.Name);
        Assert.Equal(new[] { "ready" }, registration.Tags.ToArray());
        Assert.Equal(TimeSpan.FromSeconds(5), registration.Timeout);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy)]
    [InlineData(HealthStatus.Unhealthy)]
    public async Task Liveness_AnySqlState_ReturnsHealthyWithoutCaching(HealthStatus sqlStatus)
    {
        await using var factory = CreateFactory(_ => Task.FromResult(new HealthCheckResult(sqlStatus)));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(HealthEndpoints.LivenessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Readiness_SqlReachable_ReturnsHealthy()
    {
        await using var factory = CreateFactory(_ => Task.FromResult(HealthCheckResult.Healthy()));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(HealthEndpoints.ReadinessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Readiness_SqlUnreachable_Returns503WithStatusWordOnly()
    {
        await using var factory = CreateFactory(_ => Task.FromResult(HealthCheckResult.Unhealthy("private failure detail")));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(HealthEndpoints.ReadinessPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Readiness_SqlCheckThrows_Returns503WithoutExceptionDetail()
    {
        await using var factory = CreateFactory(_ =>
            throw new InvalidOperationException("Server=private-host;Password=private-secret"));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(HealthEndpoints.ReadinessPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
    }

    // Swaps only the SQL probe, keeping the production name, tags, timeout and endpoint mapping.
    private static RentRightApplicationFactory CreateFactory(
        Func<CancellationToken, Task<HealthCheckResult>> sqlProbe) =>
        new("Production", services => services.Configure<HealthCheckServiceOptions>(options =>
        {
            var production = options.Registrations.Single(registration => registration.Name == "sql");
            options.Registrations.Remove(production);
            options.Registrations.Add(new HealthCheckRegistration(production.Name, _ => new StubCheck(sqlProbe),
                production.FailureStatus, production.Tags, production.Timeout));
        }));

    private static HttpClient CreateClient(RentRightApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });

    private sealed class StubCheck(Func<CancellationToken, Task<HealthCheckResult>> probe) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default) => probe(cancellationToken);
    }
}
