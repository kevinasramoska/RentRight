using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Data;
using RentRight.Diagnostics;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class DiagnosticsSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Fact]
    public async Task HttpRead_RealSql_RecordsCorrelatedSpansWithoutQueryOrConnectionPayloads()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = Listen(spans);
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        client.DefaultRequestHeaders.Add("traceparent", "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01");
        using var response = await client.GetAsync("/api/v1/properties?search=private-search-marker");
        response.EnsureSuccessStatusCode();
        var databaseSpans = spans.Where(span => span.TraceId.ToHexString() == "1234567890abcdef1234567890abcdef").ToArray();
        Assert.NotEmpty(databaseSpans);
        Assert.Contains(databaseSpans, span => span.OperationName == "database.reader");
        Assert.All(databaseSpans, AssertSafeSpan);
        Assert.Contains(_app.Logs, message => message.Contains("HTTP GET api/v1/properties completed with 200") &&
            message.Contains("1234567890abcdef1234567890abcdef"));
        Assert.DoesNotContain(_app.Logs, message => message.Contains("private-search-marker") ||
            message.Contains("Password=", StringComparison.OrdinalIgnoreCase) || message.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandFailure_SyncAndAsync_RecordsFailureWithoutSqlOrProviderException(bool asynchronous)
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = Listen(spans);
        using var parent = new Activity("SQL diagnostics test").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        const string sql = "SELECT [private-sql-text-marker]";
        if (asynchronous) await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        else Assert.Throws<SqlException>(() => db.Database.ExecuteSqlRaw(sql));
        var span = Assert.Single(spans, value => value.TraceId == parent.TraceId && value.OperationName == "database.non_query");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        AssertSafeSpan(span);
        Assert.Contains(_app.Logs, message => message.Contains("Database non_query failed") && message.Contains(parent.TraceId.ToHexString()));
        Assert.DoesNotContain(_app.Logs, message => message.Contains("private-sql-text-marker") || message.Contains("Invalid column", StringComparison.OrdinalIgnoreCase));
    }

    private static ActivityListener Listen(ConcurrentQueue<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RentalDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static void AssertSafeSpan(Activity span)
    {
        Assert.Equal(new[] { "db.system.name", "operation", "outcome" }, span.TagObjects.Select(value => value.Key).Order());
        Assert.Equal("microsoft.sql_server", span.GetTagItem("db.system.name"));
        Assert.True(span.Duration >= TimeSpan.Zero);
        Assert.Null(span.StatusDescription);
        Assert.Empty(span.Events);
    }
}
