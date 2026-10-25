using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentRight.Data;
using RentRight.Health;
using RentRight.Tests.Api;
using RentRight.Tests.Diagnostics;
using Xunit;

namespace RentRight.Tests.Health;

public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Liveness_FailedDependency_IsHealthyWithoutRunningChecks(string environment)
    {
        var check = new Probe(_ => Task.FromResult(HealthCheckResult.Unhealthy("private-description")));
        await using var factory = Factory(check, environment: environment);
        using var client = Client(factory);
        using var response = await client.GetAsync("/health/live");
        await AssertStatus(response, HttpStatusCode.OK, "Healthy");
        Assert.Equal(0, check.Calls);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, 200, "Healthy")]
    [InlineData(HealthStatus.Degraded, 503, "Unhealthy")]
    [InlineData(HealthStatus.Unhealthy, 503, "Unhealthy")]
    public async Task Readiness_DependencyResult_OnlyReturnsMinimalNoncacheableStatus(HealthStatus status, int expected, string body)
    {
        var check = new Probe(_ => Task.FromResult(new HealthCheckResult(status, "private-description",
            new InvalidOperationException("private-exception"), new Dictionary<string, object> { ["private-key"] = "private-value" })));
        await using var factory = Factory(check);
        using var client = Client(factory);
        using var response = await client.GetAsync("/health/ready?detail=true");
        await AssertStatus(response, (HttpStatusCode)expected, body);
        Assert.Equal(1, check.Calls);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Readiness_Timeout_CancelsCheckAndFailsWithoutDetails()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new Probe(async token =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (token.IsCancellationRequested) cancelled.TrySetResult(); }
            return HealthCheckResult.Healthy();
        });
        await using var factory = Factory(check, timeout: TimeSpan.FromMilliseconds(100));
        using var client = Client(factory);
        using var response = await client.GetAsync("/health/ready").WaitAsync(TimeSpan.FromSeconds(10));
        await AssertStatus(response, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, check.Calls);
    }

    [Fact]
    public async Task Readiness_ConcurrentProbes_AreBoundedWithoutQueueingOrBlockingLiveness()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var check = new Probe(async token =>
        {
            if (Interlocked.Increment(ref count) == 2) entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return HealthCheckResult.Healthy();
        });
        await using var factory = Factory(check, timeout: TimeSpan.FromSeconds(20));
        using var client = Client(factory);
        var first = client.GetAsync("/health/ready");
        var second = client.GetAsync("/health/ready");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var excess = await client.GetAsync("/health/ready");
            await AssertStatus(excess, HttpStatusCode.ServiceUnavailable, "Unhealthy");
            using var live = await client.GetAsync("/health/live");
            await AssertStatus(live, HttpStatusCode.OK, "Healthy");
            Assert.Equal(2, check.Calls);
        }
        finally { release.TrySetResult(); }
        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var recovered = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probe_Head_IsBodylessAndUnsafeMethodsDoNotRunChecks(string path)
    {
        var check = new Probe(_ => Task.FromResult(HealthCheckResult.Healthy()));
        await using var factory = Factory(check);
        using var client = Client(factory);
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        var before = check.Calls;
        using var post = await client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(before, check.Calls);
    }

    [Fact]
    public async Task Registration_Defaults_ChecksOnlySqlWithBoundedTimeout()
    {
        await using var factory = new RentRightApplicationFactory("Production");
        var registration = Assert.Single(factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);
        Assert.Equal("sql", registration.Name);
        Assert.Equal(new[] { "ready" }, registration.Tags);
        Assert.Equal(TimeSpan.FromSeconds(3), registration.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("private-malformed-configuration")]
    [InlineData("Server=private-host;User ID=private-user;Password=private-password")]
    [InlineData("Database=private-database")]
    public async Task SqlProbe_InvalidConfiguration_FailsWithoutExceptionOrTargetDetails(string? connectionString)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString).Options);
        var result = await new SqlReadinessHealthCheck(database).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SqlProbe_AlreadyCancelled_DoesNotWaitForConnectionOrExposeCredentials()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=private-database;User ID=private-user;Password=private-password;Connect Timeout=60").Options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new SqlReadinessHealthCheck(database).CheckHealthAsync(new HealthCheckContext(), cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task Readiness_MalformedSqlConfiguration_LogsAndRespondsWithoutProviderPayloads()
    {
        using var logs = new CapturedLogs();
        await using var factory = new RentRightApplicationFactory("Production", services =>
        {
            services.AddLogging(logging => logging.AddProvider(logs));
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer("private-malformed-configuration"));
        });
        using var client = Client(factory);
        using var response = await client.GetAsync("/health/ready");
        await AssertStatus(response, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("private-malformed-configuration", entry.Message);
            Assert.Null(entry.Exception);
        });
    }

    private static RentRightApplicationFactory Factory(Probe check, string environment = "Production", TimeSpan? timeout = null) =>
        new(environment, services => services.Configure<HealthCheckServiceOptions>(options =>
        {
            options.Registrations.Clear();
            options.Registrations.Add(new HealthCheckRegistration("test", _ => check, HealthStatus.Unhealthy,
                ["ready"], timeout ?? TimeSpan.FromSeconds(3)));
        }));

    private static HttpClient Client(RentRightApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

    private static async Task AssertStatus(HttpResponseMessage response, HttpStatusCode status, string body)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private sealed class Probe(Func<CancellationToken, Task<HealthCheckResult>> check) : IHealthCheck
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return check(cancellationToken);
        }
    }
}
