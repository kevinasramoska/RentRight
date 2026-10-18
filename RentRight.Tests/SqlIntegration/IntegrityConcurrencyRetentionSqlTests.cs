using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RentRight.Features.Maintenance;
using RentRight.Features.Properties;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class IntegrityConcurrencyRetentionSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private const string Previous = "20261017130600_MeasuredQueryIndexes";
    private const string Properties = "/api/v1/properties/";
    private readonly SqlApplication _app = new(server.ConnectionString);
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.Admin, 200)]
    [InlineData(Actor.MultiRoleAdmin, 200)]
    public async Task Property_EditResource_OnlyOwnerOrAdminReceivesPersistedVersion(Actor actor, int expected)
    {
        var property = await SeedAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        using var response = await client.GetAsync(Properties + property.Id + "/edit");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 200)
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            var body = await response.Content.ReadFromJsonAsync<Features.Properties.Contracts.PropertyEditInput>();
            Assert.Equal(Convert.ToBase64String(property.RowVersion), body!.ExpectedVersion);
            Assert.Equal(property.Title, body.Title);
            Assert.DoesNotContain("landlordId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Property_StaleEditAndDelete_ConflictWithoutOverwritingAndReloadRecovers()
    {
        var property = await SeedAsync();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        var edit = await owner.GetFromJsonAsync<Features.Properties.Contracts.PropertyEditInput>(Properties + property.Id + "/edit");
        Assert.Equal(Convert.ToBase64String(property.RowVersion), edit!.ExpectedVersion);
        using var first = await owner.PutAsJsonAsync(Properties + property.Id, Values(edit.ExpectedVersion));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var stale = await owner.PutAsJsonAsync(Properties + property.Id, Values(edit.ExpectedVersion, "Lost update"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        owner.DefaultRequestHeaders.Add("X-Expected-Version", edit.ExpectedVersion);
        using var delete = await owner.DeleteAsync(Properties + property.Id);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        await using var db = _app.CreateContext();
        var saved = await db.Properties.SingleAsync();
        Assert.Equal("Updated", saved.Title);
        Assert.NotEqual(property.RowVersion, saved.RowVersion);
        Assert.Empty(await db.MaintenanceStatusEvents.ToArrayAsync());
        owner.DefaultRequestHeaders.Remove("X-Expected-Version");
        owner.DefaultRequestHeaders.Add("X-Expected-Version", Convert.ToBase64String(saved.RowVersion));
        using var current = await owner.DeleteAsync(Properties + property.Id);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
        Assert.Empty(await db.Properties.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(null, 400)]
    [InlineData("", 400)]
    [InlineData("malformed", 400)]
    [InlineData("AAAAAAAAAAA", 400)]
    [InlineData("AAAAAAAAAAE=", 409)]
    public async Task Property_InvalidOrForgedVersions_ApiAndMvcLeaveDataUnchanged(string? version, int expected)
    {
        var property = await SeedAsync();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var api = await owner.PutAsJsonAsync(Properties + property.Id, Values(version));
        Assert.Equal(expected, (int)api.StatusCode);
        if (!string.IsNullOrEmpty(version)) owner.DefaultRequestHeaders.Add("X-Expected-Version", version);
        using var delete = await owner.DeleteAsync(Properties + property.Id);
        Assert.Equal(expected, (int)delete.StatusCode);
        using var mvc = await owner.PostAsync("/Properties/Edit/" + property.Id, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = property.Id.ToString(), ["Title"] = "Lost update", ["Address"] = "Address",
            ["RentAmount"] = "1000", ["ExpectedVersion"] = version ?? ""
        }));
        Assert.Equal(HttpStatusCode.OK, mvc.StatusCode);
        Assert.Contains(version == "AAAAAAAAAAE=" ? "changed" : "version", await mvc.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var mvcDelete = await owner.PostAsync("/Properties/DeleteConfirmed/" + property.Id, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["ExpectedVersion"] = version ?? "" }));
        Assert.Equal(expected == 400 ? HttpStatusCode.BadRequest : HttpStatusCode.Redirect, mvcDelete.StatusCode);
        await using var db = _app.CreateContext();
        var saved = await db.Properties.SingleAsync();
        Assert.Equal("Original", saved.Title);
        Assert.Equal(property.RowVersion, saved.RowVersion);
    }

    [Fact]
    public async Task Property_ValidVersionWithoutOwnership_RemainsConcealedAndFormsRequireAntiforgery()
    {
        var property = await SeedAsync();
        using var other = await ClientAsync(Actor.OtherLandlord);
        using var edit = await other.GetAsync(Properties + property.Id + "/edit");
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        using var put = await other.PutAsJsonAsync(Properties + property.Id, Values(Convert.ToBase64String(property.RowVersion)));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        using var owner = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var noCsrf = await owner.PostAsync("/Properties/DeleteConfirmed/" + property.Id, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["ExpectedVersion"] = Convert.ToBase64String(property.RowVersion) }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(property.RowVersion, (await db.Properties.SingleAsync()).RowVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Property_TwoContexts_StaleTrackedEditOrDeleteFails(bool delete)
    {
        await SeedAsync();
        await using var first = _app.CreateContext();
        await using var second = _app.CreateContext();
        var a = await first.Properties.SingleAsync();
        var b = await second.Properties.SingleAsync();
        a.Title = "First writer";
        if (delete) second.Properties.Remove(b); else b.Title = "Stale writer";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = _app.CreateContext();
        Assert.Equal("First writer", (await verify.Properties.SingleAsync()).Title);
    }

    [Fact]
    public async Task Property_ConcurrentBulkEdits_ExactlyOneWriterSucceeds()
    {
        var property = await SeedAsync();
        await using var first = _app.CreateContext();
        await using var second = _app.CreateContext();
        var values = new PropertyValues("Winner", "Address", 1000, null, property.RowVersion, "OwningLandlord");
        var results = await Task.WhenAll(new EfPropertyStore(first).UpdateAsync(property.Id, values, default),
            new EfPropertyStore(second).UpdateAsync(property.Id, values with { Title = "Other winner" }, default));
        Assert.Single(results, success => success);
        await using var verify = _app.CreateContext();
        Assert.Contains((await verify.Properties.SingleAsync()).Title, new[] { "Winner", "Other winner" });
    }

    [Fact]
    public async Task Property_ConcurrentHttpEditAndDelete_OnlyOneCurrentVersionMutationSucceeds()
    {
        var property = await SeedAsync();
        var version = Convert.ToBase64String(property.RowVersion);
        using var editor = await ClientAsync(Actor.OwningLandlord);
        using var deleter = await ClientAsync(Actor.Admin);
        deleter.DefaultRequestHeaders.Add("X-Expected-Version", version);
        var responses = await Task.WhenAll(editor.PutAsJsonAsync(Properties + property.Id, Values(version)),
            deleter.DeleteAsync(Properties + property.Id));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);
            await using var db = _app.CreateContext();
            Assert.Empty(await db.MaintenanceStatusEvents.ToArrayAsync());
            if (responses[0].IsSuccessStatusCode) Assert.Equal("Updated", (await db.Properties.SingleAsync()).Title);
            else Assert.Empty(await db.Properties.ToArrayAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Property_RazorCurrentVersions_EditAndDeleteSucceedWithRenderedTokens()
    {
        var property = await SeedAsync();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var page = await owner.GetAsync("/Properties/Edit/" + property.Id);
        var html = await page.Content.ReadAsStringAsync();
        var version = System.Net.WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"ExpectedVersion\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Equal(Convert.ToBase64String(property.RowVersion), version);
        using var edited = await owner.PostAsync("/Properties/Edit/" + property.Id, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = property.Id.ToString(), ["Title"] = "Edited", ["Address"] = "Address", ["RentAmount"] = "1000",
            ["ExpectedVersion"] = version, ["LandlordId"] = "OtherLandlord"
        }));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        using var deletionPage = await owner.GetAsync("/Properties/Delete/" + property.Id);
        var deletionHtml = await deletionPage.Content.ReadAsStringAsync();
        var current = System.Net.WebUtility.HtmlDecode(Regex.Match(deletionHtml,
            "name=\"ExpectedVersion\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEqual(version, current);
        using var deleted = await owner.PostAsync("/Properties/DeleteConfirmed/" + property.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["ExpectedVersion"] = current }));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Empty(await db.Properties.ToArrayAsync());
    }

    [Fact]
    public async Task Tenancy_AcceptAfterEndFormRead_StaleEndCannotCancelNewlyAcceptedTenancy()
    {
        var property = await SeedAsync();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var invite = await owner.PostAsJsonAsync("/api/v1/tenancies", new
        { propertyId = property.Id, tenantIdentifier = "Tenant@example.test", startDate = "2026-01-01" });
        Assert.Equal(HttpStatusCode.Accepted, invite.StatusCode);
        var receipt = await invite.Content.ReadFromJsonAsync<Controllers.Api.V1.InvitationResponse>();
        await using var db = _app.CreateContext();
        var pending = await db.Tenancies.AsNoTracking().SingleAsync();
        var staleVersion = Convert.ToBase64String(pending.RowVersion);
        using var tenant = await ClientAsync(Actor.Tenant);
        using var accepted = await tenant.PostAsJsonAsync("/api/v1/tenancies/accept", new { receipt!.Token });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        owner.DefaultRequestHeaders.Add("X-Expected-Version", staleVersion);
        using var stale = await owner.PostAsync($"/api/v1/tenancies/{pending.Id}/end", null);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await db.Tenancies.AsNoTracking().SingleAsync();
        Assert.Equal(TenancyStatus.Active, current.Status);
        Assert.Equal("Tenant", current.AcceptedByUserId);
        Assert.Null(current.EndedUtc);
        owner.DefaultRequestHeaders.Remove("X-Expected-Version");
        owner.DefaultRequestHeaders.Add("X-Expected-Version", Convert.ToBase64String(current.RowVersion));
        using var end = await owner.PostAsync($"/api/v1/tenancies/{pending.Id}/end", null);
        Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);
        Assert.Equal(current.AcceptedUtc, (await db.Tenancies.AsNoTracking().SingleAsync()).AcceptedUtc);
    }

    [Theory]
    [InlineData(null, 400)]
    [InlineData("broken", 400)]
    [InlineData("AAAAAAAAAAE=", 409)]
    public async Task Tenancy_InvalidVersions_ApiAndMvcPreserveStateAndAudit(string? version, int expected)
    {
        await SeedAsync(history: true);
        await using var db = _app.CreateContext();
        var tenancy = await db.Tenancies.SingleAsync();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        if (version is not null) owner.DefaultRequestHeaders.Add("X-Expected-Version", version);
        using var api = await owner.PostAsync($"/api/v1/tenancies/{tenancy.Id}/end", null);
        Assert.Equal(expected, (int)api.StatusCode);
        using var mvc = await owner.PostAsync("/Tenancies/End/" + tenancy.Id, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["ExpectedVersion"] = version ?? "" }));
        Assert.Equal(expected == 400 ? HttpStatusCode.BadRequest : HttpStatusCode.Redirect, mvc.StatusCode);
        await using var verify = _app.CreateContext();
        var saved = await verify.Tenancies.SingleAsync();
        Assert.Equal(tenancy.RowVersion, saved.RowVersion);
        Assert.Null(saved.EndedUtc);
        Assert.Null(saved.EndedByUserId);
        Assert.Single(await verify.MaintenanceStatusEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Maintenance_TwoContexts_StaleWriteFailsAndRevisionCannotDriftThroughBulkSql()
    {
        await SeedAsync(history: true);
        await using var first = _app.CreateContext();
        await using var second = _app.CreateContext();
        var a = await first.MaintenanceRequests.SingleAsync();
        var b = await second.MaintenanceRequests.SingleAsync();
        a.Priority = MaintenancePriority.High; a.Revision++;
        b.Priority = MaintenancePriority.Low; b.Revision++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = _app.CreateContext();
        var current = await verify.MaintenanceRequests.AsNoTracking().SingleAsync();
        Assert.Equal(1, current.Revision);
        Assert.Equal(MaintenancePriority.High, current.Priority);
        Assert.NotEqual(a.RowVersion, b.RowVersion);
        var error = await Assert.ThrowsAsync<SqlException>(() => verify.MaintenanceRequests.ExecuteUpdateAsync(u => u.SetProperty(r => r.Priority, MaintenancePriority.Low)));
        Assert.Equal(51005, error.Number);
        var unchanged = await verify.MaintenanceRequests.AsNoTracking().SingleAsync();
        Assert.Equal(current.RowVersion, unchanged.RowVersion);
        Assert.Equal(current.Revision, unchanged.Revision);
        Assert.Single(await verify.MaintenanceStatusEvents.ToArrayAsync());
        // The store must check the observed SQL token even if expectedRevision is current.
        Assert.False(await new EfMaintenanceStore(verify).TryUpdateStatusAsync(current.Id, current.Status, current.Status,
            current.LastUpdatedUtc, default, new(current.Revision, MaintenancePriority.Low, "Admin", true, null, b.RowVersion)));
        Assert.Equal(current.RowVersion, (await verify.MaintenanceRequests.AsNoTracking().SingleAsync()).RowVersion);
    }

    [Theory]
    [InlineData("UPDATE Properties SET RentAmount=-1", 547)]
    [InlineData("UPDATE Tenancies SET AcceptedUtc='2026-01-01'", 547)]
    [InlineData("UPDATE Tenancies SET InvitationTokenHash='unused'", 547)]
    [InlineData("UPDATE Tenancies SET Status='Pending'", 547)]
    [InlineData("UPDATE Tenancies SET InvitedUtc='2025-01-01'", 547)]
    [InlineData("UPDATE MaintenanceRequests SET Revision=-1", 547)]
    [InlineData("UPDATE MaintenanceRequests SET LastUpdatedUtc='2000-01-01',Revision=Revision+1", 547)]
    [InlineData("UPDATE MaintenanceRequests SET Priority='Invalid',Revision=Revision+1", 547)]
    [InlineData("UPDATE MaintenanceRequests SET Status='Invalid',Revision=Revision+1", 547)]
    [InlineData("INSERT MaintenanceStatusEvents(MaintenanceRequestId,Status,PreviousStatus,OccurredUtc,IsLegacyBaseline,ActorUserId) SELECT Id,'Closed','Submitted',LastUpdatedUtc,0,'Admin' FROM MaintenanceRequests", 547)]
    [InlineData("INSERT MaintenanceStatusEvents(MaintenanceRequestId,Status,OccurredUtc,IsLegacyBaseline,ActorUserId) SELECT Id,Status,LastUpdatedUtc,1,'Admin' FROM MaintenanceRequests", 547)]
    [InlineData("UPDATE Tenancies SET EndedByUserId='missing-actor'", 547)]
    [InlineData("DELETE MaintenanceStatusEvents", 51004)]
    [InlineData("UPDATE MaintenanceStatusEvents SET Note='Rewritten history'", 51004)]
    [InlineData("DELETE MaintenanceRequests", 547)]
    [InlineData("DELETE Tenancies", 547)]
    [InlineData("DELETE Properties", 547)]
    [InlineData("DELETE AspNetUsers WHERE Id='Tenant'", 547)]
    public async Task Sql_InvalidOrDestructiveOperations_AreRejectedWithoutPartialWrites(string sql, int number)
    {
        await SeedAsync(history: true);
        await using var db = _app.CreateContext();
        var before = await db.MaintenanceRequests.AsNoTracking().SingleAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(number, error.Number);
        var after = await db.MaintenanceRequests.AsNoTracking().SingleAsync();
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Single(await db.Properties.ToArrayAsync());
        Assert.Single(await db.Tenancies.ToArrayAsync());
        Assert.Single(await db.MaintenanceStatusEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData("DELETE Tenancies")]
    [InlineData("DELETE MaintenanceRequests")]
    public async Task Sql_HistoryWithoutDependents_IsStillRetained(string sql)
    {
        var property = await SeedAsync();
        await using var db = _app.CreateContext();
        var tenancy = new Tenancy { PropertyId = property.Id, TenantId = "Tenant", StartDate = new(2026, 1, 1), Status = TenancyStatus.Ended };
        db.Tenancies.Add(tenancy); await db.SaveChangesAsync();
        if (sql.Contains("MaintenanceRequests"))
        {
            db.MaintenanceRequests.Add(new() { TenancyId = tenancy.Id, Description = "Retained without events" });
            await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(51004, error.Number);
        Assert.Single(await db.Tenancies.ToArrayAsync());
        if (sql.Contains("MaintenanceRequests")) Assert.Single(await db.MaintenanceRequests.ToArrayAsync());
    }

    [Fact]
    public async Task Sql_AuditOnlyUserReference_PreventsDeletionWithoutRewritingProvenance()
    {
        await SeedAsync(history: true);
        await using var db = _app.CreateContext();
        db.MaintenanceStatusEvents.Add(new(MaintenanceRequestStatus.Submitted, DateTime.UtcNow,
            maintenanceRequestId: (await db.MaintenanceRequests.SingleAsync()).Id, actorUserId: "Admin"));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE AspNetUsers WHERE Id='Admin'"));
        Assert.Equal(547, error.Number);
        Assert.Contains(await db.MaintenanceStatusEvents.ToArrayAsync(), e => e.ActorUserId == "Admin");
    }

    [Fact]
    public async Task Migrate_PreviousSchema_IdempotentUpgradeRollbackAndReapplyPreserveBusinessHistory()
    {
        await using var app = new SqlApplication(server.ConnectionString);
        await app.InitializeAsync(Previous);
        await using var db = app.CreateContext();
        await SeedPreviousAsync(db);
        var migrator = db.GetService<IMigrator>();
        var script = migrator.GenerateScript(Previous, options: MigrationsSqlGenerationOptions.Idempotent);
        await db.Database.OpenConnectionAsync();
        foreach (var iteration in new[] { 1, 2 })
        {
            foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            Assert.Equal(2, await db.MaintenanceStatusEvents.CountAsync());
        }
        var property = await db.Properties.AsNoTracking().SingleAsync();
        var request = await db.MaintenanceRequests.AsNoTracking().SingleAsync();
        Assert.Equal(8, property.RowVersion.Length); Assert.Equal(8, request.RowVersion.Length);
        Assert.Equal(7, request.Revision);
        Assert.Equal("Tenant", (await db.MaintenanceStatusEvents.OrderBy(e => e.Id).FirstAsync()).ActorUserId);
        Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync(Previous);
        Assert.Equal(2, await db.MaintenanceStatusEvents.CountAsync());
        await migrator.MigrateAsync();
        Assert.Equal("Legacy", (await db.Properties.SingleAsync()).Title);
        Assert.Equal(7, (await db.MaintenanceRequests.SingleAsync()).Revision);
        Assert.Equal(2, await db.MaintenanceStatusEvents.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("UPDATE MaintenanceRequests SET LastUpdatedUtc='2000-01-01'")]
    [InlineData("UPDATE MaintenanceStatusEvents SET ActorUserId='deleted-actor'")]
    public async Task Migrate_IncompatiblePreviousRows_PreflightFailsWithoutRepairOrSchemaChanges(string invalidSql)
    {
        await using var app = new SqlApplication(server.ConnectionString);
        await app.InitializeAsync(Previous);
        await using var db = app.CreateContext();
        await SeedPreviousAsync(db);
        await db.Database.ExecuteSqlRawAsync(invalidSql);
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync());
        Assert.Equal(51003, error.Number);
        Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        await db.Database.OpenConnectionAsync();
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('Properties') AND name='RowVersion'";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        Assert.Equal(2, await db.MaintenanceStatusEvents.CountAsync());
    }

    private static Task SeedPreviousAsync(Data.ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        INSERT Properties(Title,Address,RentAmount,LandlordId) VALUES('Legacy','Address',1000,'OwningLandlord');
        INSERT Tenancies(PropertyId,TenantId,StartDate,Status) VALUES(1,'Tenant','2026-01-01','Active');
        INSERT MaintenanceRequests(TenancyId,Description,Status,Priority,Revision,DateSubmitted,LastUpdatedUtc)
        VALUES(1,'Legacy request','InProgress','High',7,'2026-01-01','2026-01-02');
        INSERT MaintenanceStatusEvents(MaintenanceRequestId,Status,PreviousStatus,OccurredUtc,IsLegacyBaseline,ActorUserId)
        VALUES(1,'Submitted',NULL,'2026-01-01',0,'Tenant'),(1,'InProgress','Submitted','2026-01-02',0,'OwningLandlord');
        """);

    private async Task<Property> SeedAsync(bool history = false)
    {
        await using var db = _app.CreateContext();
        var property = new Property { Title = "Original", Address = "Address", RentAmount = 1000, LandlordId = "OwningLandlord" };
        db.Properties.Add(property);
        if (history)
        {
            var tenancy = new Tenancy { Property = property, TenantId = "Tenant", StartDate = new(2026, 1, 1) };
            db.MaintenanceRequests.Add(new() { Tenancy = tenancy, Description = "A leaking kitchen tap", StatusHistory =
                [new(MaintenanceRequestStatus.Submitted, DateTime.UtcNow, actorUserId: "Tenant")] });
        }
        await db.SaveChangesAsync();
        return property;
    }
    private async Task<HttpClient> ClientAsync(Actor actor)
    {
        var client = await _app.CreateActorClientAsync(actor);
        await SqlApplication.AddAntiforgeryAsync(client);
        return client;
    }
    private static object Values(string? version, string title = "Updated") => new
    { title, address = "Address", rentAmount = 1100, expectedVersion = version, landlordId = "OtherLandlord" };
}
