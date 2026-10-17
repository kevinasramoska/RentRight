using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Application.Abstractions;
using RentRight.Data;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using RentRight.Features.Dashboards;
using RentRight.Features.Maintenance;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.SqlIntegration.Performance;

public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RENTRIGHT_MEASURE_QUERIES") != "1")
            Skip = "Opt in with RENTRIGHT_MEASURE_QUERIES=1; see docs/performance/QUERY_PERFORMANCE.md.";
    }
}

public sealed class QueryPerformanceTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>
{
    [PerformanceFact]
    public async Task Measure_SyntheticWorkloads_WritesSqlReadsPlansAndRepeatedTimings()
    {
        var size = ReadSize("RENTRIGHT_PERF_PROPERTIES", 1000, 100, 10000);
        var repetitions = ReadSize("RENTRIGHT_PERF_RUNS", 5, 3, 20);
        var output = Environment.GetEnvironmentVariable("RENTRIGHT_PERF_OUTPUT")
            ?? throw new InvalidOperationException("Set RENTRIGHT_PERF_OUTPUT to an artifact directory.");
        Directory.CreateDirectory(output);
        var capture = new QueryCapture();
        await using var app = new SqlApplication(server.ConnectionString, capture);
        await app.InitializeAsync("20261016161901_MaintenanceTriageAudit");
        await using var db = app.CreateContext();
        await SyntheticQueryData.SeedAsync(db, size);
        var dataset = new
        {
            Properties = await db.Properties.CountAsync(), Tenancies = await db.Tenancies.CountAsync(),
            Requests = await db.MaintenanceRequests.CountAsync(), History = await db.MaintenanceStatusEvents.CountAsync(),
            Users = await db.Users.CountAsync(), Assignments = await db.UserRoles.CountAsync()
        };
        var storage = new List<IndexStorage>();
        var workloads = Workloads(app);
        var evidence = new List<WorkloadEvidence>();
        foreach (var phase in new[] { "baseline", "property-index", "property-and-queue-index", "final-migration" })
        {
            if (phase == "property-index")
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IX_Perf_Properties ON Properties (Title,Address,Id)
                    INCLUDE (RentAmount,Description,LandlordId);
                    """);
            if (phase == "property-and-queue-index")
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IX_Perf_Maintenance ON MaintenanceRequests (Status,DateSubmitted DESC,Id DESC)
                    INCLUDE (TenancyId,Priority,Revision);
                    """);
            if (phase == "final-migration")
                await db.Database.ExecuteSqlRawAsync("""
                    DROP INDEX IX_Perf_Properties ON Properties;
                    DROP INDEX IX_Perf_Maintenance ON MaintenanceRequests;
                    """);
            if (phase == "final-migration")
            {
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                Assert.False(db.Database.HasPendingModelChanges());
            }
            await using (var connection = new SqlConnection(db.Database.GetConnectionString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT OBJECT_NAME(i.object_id),i.name,SUM(p.used_page_count)*8
                    FROM sys.indexes i JOIN sys.dm_db_partition_stats p
                        ON p.object_id=i.object_id AND p.index_id=i.index_id
                    WHERE OBJECT_NAME(i.object_id) IN ('Properties','MaintenanceRequests')
                    GROUP BY i.object_id,i.name ORDER BY OBJECT_NAME(i.object_id),i.name;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) storage.Add(new(phase, reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
            }
            foreach (var workload in workloads)
            {
                // Container is unique to this test class. These commands never target a configured application server.
                await db.Database.ExecuteSqlRawAsync("CHECKPOINT; DBCC DROPCLEANBUFFERS WITH NO_INFOMSGS; DBCC FREEPROCCACHE WITH NO_INFOMSGS;");
                capture.Commands.Clear();
                capture.Enabled = true;
                var watch = Stopwatch.StartNew();
                await workload.Run();
                var cold = watch.Elapsed.TotalMilliseconds;
                Assert.Equal(workload.Commands, capture.Commands.Count);
                var commands = capture.Commands.ToArray();
                capture.Enabled = false;
                var warm = new List<double>();
                for (var run = 0; run < repetitions; run++)
                {
                    watch.Restart();
                    await workload.Run();
                    warm.Add(watch.Elapsed.TotalMilliseconds);
                }
                await using var connection = new SqlConnection(db.Database.GetConnectionString());
                await connection.OpenAsync();
                var sql = new List<CommandEvidence>();
                foreach (var command in commands) sql.Add(await command.ExplainAsync(connection));
                evidence.Add(new(phase, workload.Name, commands.Length, cold, warm, sql));
                await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"),
                    JsonSerializer.Serialize(new { Dataset = dataset, WarmRuns = repetitions, Storage = storage, Evidence = evidence },
                        new JsonSerializerOptions { WriteIndented = false }));
            }
        }
    }

    private static int ReadSize(string name, int fallback, int minimum, int maximum)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (raw is null) return fallback;
        if (!int.TryParse(raw, out var result) || result < minimum || result > maximum)
            throw new InvalidOperationException($"{name} must be between {minimum} and {maximum}.");
        return result;
    }

    private static List<Workload> Workloads(SqlApplication app)
    {
        async Task WithContext(Func<ApplicationDbContext, Task> run)
        {
            await using var db = app.CreateContext();
            await run(db);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        Workload Properties(string name, PublishedPropertyQuery query) => new(name, 2,
            () => WithContext(async db => { await new EfPropertyStore(db).SearchPublishedAsync(query, default); }));
        Workload Queue(string name, QueryUser user, MaintenanceQueueQuery query) => new(name, 2,
            () => WithContext(async db =>
            {
                var result = await new LandlordMaintenanceService(db, user).ListAsync(query, default);
                Assert.Equal(TenantMaintenanceStatus.Success, result.Status);
            }));
        Workload Admin(int size) => new($"admin-users-{size}", 4, async () =>
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAdministrationRoleStore>().GetUsersPageAsync(1, size, default);
        });
        return
        [
            new("properties-mvc", 1, () => WithContext(async db => { await new EfPropertyStore(db).ListAsync(100, default); })),
            new("properties-home", 1, () => WithContext(async db => { await new EfPropertyStore(db).ListLatestAsync(6, default); })),
            Properties("properties-page-1", new(1, 25)),
            Properties("properties-page-20", new(20, 25)),
            Properties("properties-search-rent", new(1, 25, "Home 1", 900, 1600)),
            new("tenant-dashboard-hot", 3, () => WithContext(async db =>
            {
                var result = await new TenantDashboardQueryService(new EfDashboardDataStore(db),
                    new QueryUser("Tenant", "Tenant"), new QueryClock()).QueryAsync(default);
                Assert.Equal(5, result.Dashboard!.RecentMaintenanceRequests.Count);
            })),
            new("tenant-dashboard-idle", 3, () => WithContext(async db =>
            {
                await new TenantDashboardQueryService(new EfDashboardDataStore(db),
                    new QueryUser("synthetic-7", "Tenant"), new QueryClock()).QueryAsync(default);
            })),
            Queue("owner-queue", new("OwningLandlord", "Landlord"), new()),
            Queue("owner-queue-filter-page-2", new("OwningLandlord", "Landlord"),
                new() { Status = "Submitted", Page = 2, SubmittedFrom = new(2026, 9, 1), SubmittedTo = new(2026, 10, 17) }),
            Queue("owner-queue-property", new("OwningLandlord", "Landlord"), new() { PropertyId = 1, PageSize = 100 }),
            Queue("admin-queue-filter", new("MultiRoleAdmin", "Admin", "Landlord", "Tenant"), new() { Status = "Submitted" }),
            Admin(1), Admin(50)
        ];
    }

    private sealed record IndexStorage(string Phase, string Table, string Index, long UsedKiB);
    private sealed record Workload(string Name, int Commands, Func<Task> Run);
    private sealed record WorkloadEvidence(string Phase, string Workload, int Commands,
        double ColdMilliseconds, List<double> WarmMilliseconds, List<CommandEvidence> Sql);
}

internal sealed class QueryUser(string id, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public string? UserId => id;
    public bool IsInRole(string roleName) => roles.Contains(roleName);
}

internal sealed class QueryClock : IClock
{
    public DateTime UtcNow => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
}
