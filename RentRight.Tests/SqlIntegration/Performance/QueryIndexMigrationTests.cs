using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RentRight.Data;
using Xunit;

namespace RentRight.Tests.SqlIntegration.Performance;

[Trait("Category", "SqlIntegration")]
public sealed class QueryIndexMigrationTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task Migrate_PreviousSchemaWithHistory_UpDownAndReapplyPreserveDataAndFilteredConstraints()
    {
        const string previous = "20261016161901_MaintenanceTriageAudit";
        await using var app = new SqlApplication(server.ConnectionString);
        await app.InitializeAsync(previous);
        await using var db = app.CreateContext();
        await SyntheticQueryData.SeedAsync(db, 100);
        var before = await DataAsync(db);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(before, await DataAsync(db));
        await migrator.MigrateAsync(previous);
        Assert.Equal(before, await DataAsync(db));
        await migrator.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(before, await DataAsync(db));

        // The query migration must not alter the reservation/token filtered-unique indexes.
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name,filter_definition,is_unique FROM sys.indexes
            WHERE object_id=OBJECT_ID('Tenancies') AND filter_definition IS NOT NULL ORDER BY name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var filters = new List<string>();
        while (await reader.ReadAsync())
        {
            Assert.True(reader.GetBoolean(2));
            filters.Add(reader.GetString(1));
        }
        Assert.Equal(2, filters.Count);
        Assert.Contains(filters, f => f.Contains("InvitationTokenHash") && f.Contains("IS NOT NULL"));
        Assert.Contains(filters, f => f.Contains("Active") && f.Contains("Pending"));
    }

    private static async Task<string> DataAsync(ApplicationDbContext db) => JsonSerializer.Serialize(new
    {
        Properties = await db.Properties.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
        Tenancies = await db.Tenancies.AsNoTracking().OrderBy(t => t.Id).ToArrayAsync(),
        Requests = await db.MaintenanceRequests.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
        History = await db.MaintenanceStatusEvents.AsNoTracking().OrderBy(e => e.Id).ToArrayAsync(),
        Roles = await db.UserRoles.AsNoTracking().OrderBy(r => r.UserId).ThenBy(r => r.RoleId).ToArrayAsync()
    });
}
