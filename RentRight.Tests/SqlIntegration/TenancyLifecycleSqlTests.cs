using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Controllers.Api.V1;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class TenancyLifecycleSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    private const string Route = "/api/v1/tenancies";
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 202)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.MultiRoleAdmin, 202)]
    [InlineData(Actor.Admin, 202)]
    public async Task Invite_ActorMatrix_UsesPersistedOwnershipAndServerAudit(Actor actor, int expected)
    {
        var property = await PropertyAsync();
        using var client = await ClientAsync(actor);
        using var response = await client.PostAsJsonAsync(Route, Input(property));
        Assert.Equal(expected, (int)response.StatusCode);
        await using var db = _app.CreateContext();
        if (expected != 202) { Assert.Empty(await db.Tenancies.ToListAsync()); return; }
        var receipt = await response.Content.ReadFromJsonAsync<InvitationResponse>();
        var row = await db.Tenancies.SingleAsync();
        Assert.Equal(TenancyStatus.Pending, row.Status);
        Assert.Equal(Actor.Tenant.ToString(), row.TenantId);
        Assert.Equal(actor.ToString(), row.InvitedByUserId);
        Assert.Null(row.AcceptedUtc);
        Assert.Null(row.EndedUtc);
        Assert.Equal(64, row.InvitationTokenHash!.Length);
        Assert.NotEqual(receipt!.Token, row.InvitationTokenHash);
        Assert.Equal(TimeSpan.FromDays(7), row.InvitationExpiresUtc - row.InvitedUtc);
        Assert.Equal(receipt.ExpiresUtc, row.InvitationExpiresUtc);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain(receipt.Token, JsonSerializer.Serialize(row));
        using var get = await client.GetAsync(Route + "/" + row.Id);
        var publicJson = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(receipt.Token, publicJson);
        Assert.DoesNotContain("tenantId", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tenant@example.test", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(receipt.Token, string.Join('\n', _app.Logs));
    }

    [Fact]
    public async Task Invite_UnknownAndNonTenantIdentifiers_HaveIndistinguishableReceiptsAndPrivateLists()
    {
        using var owner = await ClientAsync(Actor.OwningLandlord);
        foreach (var identifier in new[] { "unknown@example.test", "OtherLandlord@example.test", "Tenant@example.test" })
        {
            var property = await PropertyAsync();
            using var response = await owner.PostAsJsonAsync(Route, Input(property, identifier));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<InvitationResponse>();
            Assert.Equal(64, receipt!.Token.Length);
            Assert.Equal(new[] { "expiresUtc", "token" }, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            using var page = await owner.GetAsync("/Tenancies");
            var html = await page.Content.ReadAsStringAsync();
            Assert.DoesNotContain(identifier, html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(receipt.Token, html);
        }
        using var form = await owner.GetAsync("/Tenancies/Create");
        var createHtml = await form.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Tenant@example.test", createHtml, StringComparison.OrdinalIgnoreCase);
        await using var db = _app.CreateContext();
        Assert.Equal(2, await db.Tenancies.CountAsync(t => t.TenantId == null));
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 200)]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.MultiRoleAdmin, 200)]
    [InlineData(Actor.Admin, 200)]
    public async Task Read_ActiveTenancyActorMatrix_ScopesListAndConcealsDetail(Actor actor, int expected)
    {
        var id = await ActiveAsync();
        using var client = await ClientAsync(actor);
        using var detail = await client.GetAsync($"{Route}/{id}");
        Assert.Equal(expected, (int)detail.StatusCode);
        using var list = await client.GetAsync(Route + "?pageSize=1");
        if (actor == Actor.Anonymous) { Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode); return; }
        list.EnsureSuccessStatusCode();
        var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(expected == 200 ? 1 : 0, page.GetProperty("totalCount").GetInt32());
        using var missing = await client.GetAsync(Route + "/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 204)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.MultiRoleAdmin, 204)]
    [InlineData(Actor.Admin, 204)]
    public async Task End_ActorMatrix_RetainsOccupancyAndAudit(Actor actor, int expected)
    {
        var id = await ActiveAsync();
        using var client = await ClientAsync(actor);
        using var response = await client.PostAsync($"{Route}/{id}/end", null);
        Assert.Equal(expected, (int)response.StatusCode);
        await using var db = _app.CreateContext();
        var row = await db.Tenancies.SingleAsync();
        Assert.Equal(expected == 204 ? TenancyStatus.Ended : TenancyStatus.Active, row.Status);
        Assert.Equal(expected == 204 ? actor.ToString() : null, row.EndedByUserId);
        Assert.Equal(Actor.Tenant.ToString(), row.TenantId);
        if (expected == 204)
        {
            Assert.NotNull(row.EndedUtc);
            using var again = await client.PostAsync($"{Route}/{id}/end", null);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            using var tenant = await ClientAsync(Actor.Tenant);
            using var detail = await tenant.GetAsync($"{Route}/{id}");
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            using var delete = await client.DeleteAsync("/api/v1/properties/" + row.PropertyId);
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        }
    }

    [Theory]
    [InlineData("wrong-tenant", 404)]
    [InlineData("invalid", 404)]
    [InlineData("expired", 409)]
    [InlineData("past-end", 409)]
    [InlineData("role-removed", 404)]
    [InlineData("non-tenant", 403)]
    [InlineData("unknown", 404)]
    public async Task Accept_InvalidOrMismatchedInvitation_DoesNotActivate(string scenario, int expected)
    {
        var property = await PropertyAsync();
        var receipt = await InviteAsync(property, scenario == "unknown" ? "unknown@example.test" : "Tenant@example.test");
        using var tenant = await ClientAsync(scenario == "wrong-tenant" ? Actor.MultiRoleAdmin : scenario == "non-tenant" ? Actor.OwningLandlord : Actor.Tenant);
        await using (var db = _app.CreateContext())
        {
            var row = await db.Tenancies.SingleAsync();
            if (scenario == "expired")
            {
                row.InvitedUtc = DateTime.UtcNow.AddDays(-8);
                row.InvitationExpiresUtc = DateTime.UtcNow.AddDays(-1);
            }
            if (scenario == "past-end") row.EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }
        if (scenario == "role-removed")
        {
            // Keep the already-issued cookie to prove the persisted role is rechecked.
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByIdAsync("Tenant"))!, "Tenant")).Succeeded);
        }
        using var result = await tenant.PostAsJsonAsync(Route + "/accept", new { token = scenario == "invalid" ? new string('B', 64) : receipt.Token });
        Assert.Equal(expected, (int)result.StatusCode);
        await using var verification = _app.CreateContext();
        Assert.Equal(TenancyStatus.Pending, (await verification.Tenancies.SingleAsync()).Status);
        Assert.DoesNotContain(receipt.Token, await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lifecycle_CookieAndAntiforgery_RequiresConsentAndPreservesMaintenanceAuthorization()
    {
        var property = await PropertyAsync();
        using var owner = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var noCsrfInvite = await owner.PostAsJsonAsync(Route, Input(property));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrfInvite.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(owner);
        var receipt = await InviteAsync(property);
        int id;
        await using (var db = _app.CreateContext()) id = (await db.Tenancies.SingleAsync()).Id;
        using var tenant = await _app.CreateActorClientAsync(Actor.Tenant);
        using var pendingRead = await tenant.GetAsync($"{Route}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, pendingRead.StatusCode);
        using var pendingDashboard = await tenant.GetAsync("/Dashboard/TenantDashboard");
        pendingDashboard.EnsureSuccessStatusCode();
        Assert.DoesNotContain("Test Street", await pendingDashboard.Content.ReadAsStringAsync());
        using var noCsrfAccept = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt.Token });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrfAccept.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(tenant);
        using var accept = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt.Token, tenantId = "OtherLandlord", status = "Ended", acceptedByUserId = "Admin" });
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        using var activeDashboard = await tenant.GetAsync("/Dashboard/TenantDashboard");
        activeDashboard.EnsureSuccessStatusCode();
        Assert.Contains("Test Street", await activeDashboard.Content.ReadAsStringAsync());
        using var reused = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt.Token });
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        await using (var db = _app.CreateContext())
        {
            var row = await db.Tenancies.SingleAsync();
            Assert.Equal("Tenant", row.AcceptedByUserId);
            Assert.NotNull(row.AcceptedUtc);
            Assert.Null(row.InvitationTokenHash);
            db.MaintenanceRequests.Add(new() { TenancyId = id, Description = "Synthetic maintenance description", DateSubmitted = DateTime.UtcNow, LastUpdatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var maintenance = await tenant.GetAsync("/MaintenanceRequest");
        maintenance.EnsureSuccessStatusCode();
        Assert.Contains("Synthetic maintenance description", await maintenance.Content.ReadAsStringAsync());
        using var other = await ClientAsync(Actor.OtherLandlord);
        using var concealed = await other.GetAsync("/MaintenanceRequest/Details/1");
        Assert.Equal(HttpStatusCode.NotFound, concealed.StatusCode);
        using var missingCsrfEndClient = await _app.CreateActorClientAsync(Actor.Admin);
        using var noCsrfEnd = await missingCsrfEndClient.PostAsync($"{Route}/{id}/end", null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrfEnd.StatusCode);
        using var end = await owner.PostAsync($"{Route}/{id}/end", null);
        Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);
        await using var history = _app.CreateContext();
        Assert.Single(await history.MaintenanceRequests.ToListAsync());
        Assert.Equal(TenancyStatus.Ended, (await history.Tenancies.SingleAsync()).Status);
        Assert.DoesNotContain(receipt.Token, string.Join('\n', _app.Logs));
    }

    [Fact]
    public async Task Invite_InvalidDatesMissingPropertyAndDuplicate_MapsProblemDetails()
    {
        using var owner = await ClientAsync(Actor.OwningLandlord);
        var property = await PropertyAsync();
        using var invalid = await owner.PostAsJsonAsync(Route, new { propertyId = property, tenantIdentifier = "Tenant@example.test", startDate = "2026-10-20", endDate = "2026-10-19" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("validation_failed", JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        using var missing = await owner.PostAsJsonAsync(Route, Input(int.MaxValue));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await InviteAsync(property);
        using var duplicate = await owner.PostAsJsonAsync(Route, Input(property));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var noEnd = await owner.PostAsync(Route + "/2147483647/end", null);
        Assert.Equal(HttpStatusCode.NotFound, noEnd.StatusCode);
    }

    [Fact]
    public async Task Invite_ExpiredReservation_ReplacesWithoutDeletingHistory()
    {
        var property = await PropertyAsync();
        var first = await InviteAsync(property);
        await using (var db = _app.CreateContext())
        {
            var old = await db.Tenancies.SingleAsync();
            old.InvitedUtc = DateTime.UtcNow.AddDays(-8);
            old.InvitationExpiresUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var second = await InviteAsync(property);
        Assert.NotEqual(first.Token, second.Token);
        await using var verification = _app.CreateContext();
        var history = await verification.Tenancies.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(TenancyStatus.Ended, history[0].Status);
        Assert.Equal("Expired", history[0].EndReason);
        Assert.Null(history[0].EndedByUserId);
        Assert.NotNull(history[0].EndedUtc);
        Assert.Null(history[0].InvitationTokenHash);
        Assert.Equal(TenancyStatus.Pending, history[1].Status);
    }

    [Theory]
    [InlineData("invite-invite")]
    [InlineData("accept-accept")]
    [InlineData("end-end")]
    [InlineData("accept-end")]
    [InlineData("invite-accept")]
    [InlineData("invite-end")]
    public async Task Lifecycle_ConcurrentCommands_EnforcesSingleOccupancyAndNoLostAudit(string scenario)
    {
        var property = await PropertyAsync();
        InvitationResponse? receipt = null;
        int id = 0;
        if (scenario != "invite-invite")
        {
            receipt = await InviteAsync(property);
            await using var db = _app.CreateContext();
            id = (await db.Tenancies.SingleAsync()).Id;
        }
        using var owner1 = await ClientAsync(Actor.OwningLandlord);
        using var owner2 = await ClientAsync(Actor.Admin);
        using var tenant1 = await ClientAsync(Actor.Tenant);
        using var tenant2 = await ClientAsync(Actor.Tenant);
        if (scenario is "end-end" or "invite-end")
        {
            using var accepted = await tenant1.PostAsJsonAsync(Route + "/accept", new { receipt!.Token });
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Run(Func<Task<HttpResponseMessage>> action) { await gate.Task; return await action(); }
        var first = Run(() => scenario.StartsWith("invite") ? owner1.PostAsJsonAsync(Route, Input(property))
            : scenario.StartsWith("accept") ? tenant1.PostAsJsonAsync(Route + "/accept", new { receipt!.Token })
            : owner1.PostAsync($"{Route}/{id}/end", null));
        var second = Run(() => scenario.EndsWith("invite") ? owner2.PostAsJsonAsync(Route, Input(property))
            : scenario.EndsWith("accept") ? tenant2.PostAsJsonAsync(Route + "/accept", new { receipt!.Token })
            : owner2.PostAsync($"{Route}/{id}/end", null));
        gate.SetResult();
        using var a = await first;
        using var b = await second;
        Assert.Contains((int)a.StatusCode, new[] { 202, 204, 404, 409 });
        Assert.Contains((int)b.StatusCode, new[] { 202, 204, 404, 409 });
        if (scenario is "invite-invite" or "accept-accept" or "end-end")
        {
            Assert.Single(new[] { a, b }, r => r.IsSuccessStatusCode);
        }
        if (scenario == "invite-accept") { Assert.Equal(HttpStatusCode.Conflict, a.StatusCode); Assert.Equal(HttpStatusCode.NoContent, b.StatusCode); }
        await using var verification = _app.CreateContext();
        Assert.InRange(await verification.Tenancies.CountAsync(t => t.Status == TenancyStatus.Active || t.Status == TenancyStatus.Pending), 0, 1);
        var original = await verification.Tenancies.OrderBy(t => t.Id).FirstAsync();
        Assert.NotNull(original.InvitedUtc);
        if (scenario.Contains("end")) { Assert.Equal(TenancyStatus.Ended, original.Status); Assert.NotNull(original.EndedUtc); Assert.NotNull(original.EndedByUserId); }
        if (original.AcceptedUtc is not null) Assert.Equal("Tenant", original.AcceptedByUserId);
    }

    [Fact]
    public async Task Save_ReservationConstraintAndRowversion_RejectsDuplicateAndStaleWrites()
    {
        var property = await PropertyAsync();
        await InviteAsync(property);
        await using (var db = _app.CreateContext())
        {
            db.Tenancies.Add(new() { PropertyId = property, TenantId = "Tenant", StartDate = new(2026, 1, 1), Status = TenancyStatus.Active });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }
        await using var first = _app.CreateContext();
        await using var second = _app.CreateContext();
        var a = await first.Tenancies.SingleAsync();
        var b = await second.Tenancies.SingleAsync();
        a.End("Admin", DateTime.UtcNow);
        b.End("OwningLandlord", DateTime.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Migrate_LegacyOccupancies_PreservesDataAndDoesNotInventConsent()
    {
        await using var db = _app.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260811163855_CompleteCoreRentalDomain");
        // Raw SQL deliberately targets the old schema before the new rowversion/audit columns exist.
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [Properties] ([Title],[Address],[RentAmount],[LandlordId]) VALUES (N'Legacy',N'Address',1000,N'OwningLandlord')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [Tenancies] ([PropertyId],[TenantId],[StartDate],[EndDate],[Status]) VALUES (1,N'Tenant','2026-01-01',NULL,N'Active'),(1,N'Tenant','2025-01-01','2025-12-31',N'Ended')");
        await migrator.MigrateAsync();
        var rows = await db.Tenancies.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(TenancyStatus.Active, rows[0].Status);
        Assert.Equal(TenancyStatus.Ended, rows[1].Status);
        Assert.All(rows, row => { Assert.Null(row.InvitedUtc); Assert.Null(row.AcceptedUtc); Assert.Null(row.EndedUtc); Assert.NotEmpty(row.RowVersion); Assert.Equal("Tenant", row.TenantId); });
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migrate_RollbackWithLifecycleAudit_StopsBeforeDataLoss()
    {
        await using var db = _app.CreateContext();
        // Exercise this migration's own downgrade boundary. Newer migrations can
        // legitimately commit their Down before the tenancy guard is reached.
        await db.GetService<IMigrator>().MigrateAsync("20261016115250_TenancyInvitationLifecycle");
        await InviteAsync(await PropertyAsync());
        var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var exception = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20260811163855_CompleteCoreRentalDomain"));
        Assert.Equal(51000, exception.Number);
        var tenancy = await db.Tenancies.SingleAsync();
        Assert.Equal(TenancyStatus.Pending, tenancy.Status);
        Assert.NotNull(tenancy.InvitedUtc);
        Assert.NotNull(tenancy.InvitationTokenHash);
        Assert.Equal(appliedBefore, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [Fact]
    public async Task Razor_AdminInviteAndTenantAccept_UsesOneTimeReceiptAndCookieAntiforgery()
    {
        var property = await PropertyAsync();
        using var admin = await ClientAsync(Actor.Admin);
        using var form = await admin.GetAsync("/Tenancies/Create");
        form.EnsureSuccessStatusCode();
        using var invite = await admin.PostAsync("/Tenancies/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PropertyId"] = property.ToString(), ["TenantId"] = "Tenant@example.test", ["StartDate"] = "2026-01-01"
        }));
        invite.EnsureSuccessStatusCode();
        var html = await invite.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "readonly>([A-F0-9]{64})</textarea>").Groups[1].Value;
        Assert.Equal(64, token.Length);
        Assert.True(invite.Headers.CacheControl!.NoStore);
        using var tenant = await ClientAsync(Actor.Tenant);
        using var acceptForm = await tenant.GetAsync("/Tenancies/Accept");
        acceptForm.EnsureSuccessStatusCode();
        using var accepted = await tenant.PostAsync("/Tenancies/Accept", new FormUrlEncodedContent(new Dictionary<string, string> { ["Token"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        await using var db = _app.CreateContext();
        var row = await db.Tenancies.SingleAsync();
        Assert.Equal(TenancyStatus.Active, row.Status);
        using var end = await admin.PostAsync("/Tenancies/End/" + row.Id, null);
        Assert.Equal(HttpStatusCode.Redirect, end.StatusCode);
    }

    [Theory]
    [InlineData(" tenant@EXAMPLE.TEST ", 204)]
    [InlineData("Tenant@", 404)]
    [InlineData("Tenant@example.test.extra", 404)]
    public async Task Invite_ExactNormalizedIdentifier_RejectsPartialMatches(string identifier, int expected)
    {
        var receipt = await InviteAsync(await PropertyAsync(), identifier);
        using var tenant = await ClientAsync(Actor.Tenant);
        using var response = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt.Token });
        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("active-without-tenant", 547)]
    [InlineData("pending-without-expiry", 547)]
    [InlineData("duplicate-token-hash", 2601)]
    public async Task Save_InvalidLifecycleState_DatabaseRejectsIt(string scenario, int expected)
    {
        var property = await PropertyAsync();
        if (scenario == "duplicate-token-hash") await InviteAsync(await PropertyAsync());
        await using var db = _app.CreateContext();
        var row = new Tenancy
        {
            PropertyId = property, StartDate = new(2026, 1, 1), Status = TenancyStatus.Pending,
            InvitedUtc = DateTime.UtcNow, InvitationExpiresUtc = DateTime.UtcNow.AddDays(7),
            InvitedByUserId = "Admin", InvitationTokenHash = new string('A', 64)
        };
        if (scenario == "active-without-tenant") row.Status = TenancyStatus.Active;
        if (scenario == "pending-without-expiry") row.InvitationExpiresUtc = null;
        if (scenario == "duplicate-token-hash") row.InvitationTokenHash = (await db.Tenancies.SingleAsync()).InvitationTokenHash;
        db.Tenancies.Add(row);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(expected, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Fact]
    public async Task Accept_FutureTenancy_ReservesPropertyWithoutGrantingCurrentAccess()
    {
        var property = await PropertyAsync();
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var invitation = await owner.PostAsJsonAsync(Route, new { propertyId = property, tenantIdentifier = "Tenant@example.test", startDate = start });
        Assert.Equal(HttpStatusCode.Accepted, invitation.StatusCode);
        var receipt = await invitation.Content.ReadFromJsonAsync<InvitationResponse>();
        using var tenant = await ClientAsync(Actor.Tenant);
        using var accepted = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt!.Token });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        await using var db = _app.CreateContext();
        var row = await db.Tenancies.SingleAsync();
        Assert.Equal(TenancyStatus.Active, row.Status);
        Assert.False(row.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)));
        using var detail = await tenant.GetAsync($"{Route}/{row.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var duplicate = await owner.PostAsJsonAsync(Route, Input(property));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var ended = await owner.PostAsync($"{Route}/{row.Id}/end", null);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        await db.Entry(row).ReloadAsync();
        Assert.Equal(start, row.EndDate);
    }

    private async Task<HttpClient> ClientAsync(Actor actor)
    {
        var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        return client;
    }
    private async Task<int> PropertyAsync()
    {
        await using var db = _app.CreateContext();
        var property = new Property { Title = "Home", Address = "Test Street", LandlordId = "OwningLandlord", RentAmount = 1000 };
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }
    private static object Input(int property, string identifier = "Tenant@example.test") => new
    {
        propertyId = property, tenantIdentifier = identifier, startDate = "2026-01-01",
        tenantId = "OtherLandlord", invitedByUserId = "OtherLandlord", status = "Active", acceptedUtc = "2020-01-01T00:00:00Z"
    };
    private async Task<InvitationResponse> InviteAsync(int property, string identifier = "Tenant@example.test")
    {
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var response = await owner.PostAsJsonAsync(Route, Input(property, identifier));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationResponse>())!;
    }
    private async Task<int> ActiveAsync()
    {
        var receipt = await InviteAsync(await PropertyAsync());
        using var tenant = await ClientAsync(Actor.Tenant);
        using var accepted = await tenant.PostAsJsonAsync(Route + "/accept", new { receipt.Token });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        await using var db = _app.CreateContext();
        return (await db.Tenancies.SingleAsync()).Id;
    }
}
