using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RentRight.Controllers.Api.V1;
using RentRight.Features.Dashboards;
using RentRight.Features.Maintenance;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class TenantMaintenanceSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    private const string Route = "/api/v1/maintenance-requests";
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Fact]
    public async Task Submit_AcceptedTenancy_DerivesFieldsCreatesAtomicHistoryAndAllowsRepeatedDescriptions()
    {
        var tenancy = await TenancyAsync("accepted");
        using var client = await ClientAsync(Actor.Tenant);
        var before = DateTime.UtcNow;
        const string description = "<script>alert('untrusted')</script> Kitchen leak";
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PostAsJsonAsync(Route, new { description, tenancyId = 999,
                tenantId = "MultiRoleAdmin", landlordId = "OtherLandlord", status = "Closed", submittedUtc = "2000-01-01T00:00:00Z" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<MaintenanceSubmissionResponse>();
            using var detail = await client.GetAsync(response.Headers.Location);
            detail.EnsureSuccessStatusCode();
            var result = await detail.Content.ReadFromJsonAsync<TenantMaintenanceDetail>();
            Assert.Equal("Submitted", result!.Request.Status);
            Assert.Equal("Submitted", Assert.Single(result.History).Status);
            Assert.False(result.History[0].IsLegacyBaseline);
            using var razor = await client.GetAsync("/MaintenanceRequest/Details/" + receipt!.Id);
            razor.EnsureSuccessStatusCode();
            var html = await razor.Content.ReadAsStringAsync();
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain(description, html);
            Assert.Contains("Status history", html);
        }
        await using var db = _app.CreateContext();
        var requests = await db.MaintenanceRequests.Include(r => r.StatusHistory).ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r =>
        {
            Assert.Equal(tenancy, r.TenancyId);
            Assert.Equal(MaintenanceRequestStatus.Submitted, r.Status);
            Assert.InRange(r.DateSubmitted, before, DateTime.UtcNow);
            Assert.Equal(r.DateSubmitted, r.LastUpdatedUtc);
            Assert.Equal(r.DateSubmitted, Assert.Single(r.StatusHistory).OccurredUtc);
        });
        Assert.DoesNotContain(description, string.Join('\n', _app.Logs));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("pending")]
    [InlineData("declined")]
    [InlineData("expired-invitation")]
    [InlineData("expired")]
    [InlineData("ended")]
    [InlineData("future")]
    [InlineData("unaccepted")]
    public async Task TenantJourney_IneligibleTenancy_HasEmptyDashboardAndReadsAndRejectsSubmission(string scenario)
    {
        int? requestId = null;
        if (scenario != "none") requestId = await RequestAsync(await TenancyAsync(scenario));
        using var client = await ClientAsync(Actor.Tenant);
        using var submit = await client.PostAsJsonAsync(Route, new { description = "A leaking kitchen sink" });
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        using var problem = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        Assert.Equal("no_active_tenancy", problem.RootElement.GetProperty("code").GetString());
        using var list = await client.GetAsync(Route);
        var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        if (requestId is not null)
        {
            using var detail = await client.GetAsync(Route + "/" + requestId);
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            using var razor = await client.GetAsync("/MaintenanceRequest/Details/" + requestId);
            Assert.Equal(HttpStatusCode.NotFound, razor.StatusCode);
        }
        using var dashboard = await client.GetAsync("/Dashboard/TenantDashboard");
        dashboard.EnsureSuccessStatusCode();
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("do not have a currently active tenancy", html);
        Assert.DoesNotContain("Submit Request</a>", html);
        Assert.Contains("<strong>0</strong> open requests", html);
        using var form = await client.GetAsync("/MaintenanceRequest/Create");
        Assert.Contains("do not have a currently active tenancy", await form.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_ActiveTenancy_ProjectsAccurateCountsAndDeterministicRecentRequests()
    {
        var tenancy = await TenancyAsync("accepted");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 7; i++)
            await RequestAsync(tenancy, i < 2 ? MaintenanceRequestStatus.Submitted : MaintenanceRequestStatus.Closed,
                i == 0 ? now.AddDays(-31) : now);
        var other = await TenancyAsync("accepted", "MultiRoleAdmin");
        await RequestAsync(other);
        await using var db = _app.CreateContext();
        var store = new EfDashboardDataStore(db);
        var today = DateOnly.FromDateTime(now);
        var counts = await store.GetTenantMaintenanceCountsAsync("Tenant", today, now.AddDays(-30), default);
        Assert.Equal(2, counts.Open);
        Assert.Equal(6, counts.Recent);
        var recent = await store.ListTenantMaintenanceAsync("Tenant", today, 5, default);
        Assert.Equal(5, recent.Count);
        Assert.Equal(recent.OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id), recent);
        Assert.Single(await store.ListTenantTenanciesAsync("Tenant", today, 100, default));
        Assert.Empty(db.ChangeTracker.Entries());
        using var client = await ClientAsync(Actor.Tenant);
        using var dashboard = await client.GetAsync("/Dashboard/TenantDashboard");
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("<strong>2</strong> open requests", html);
        Assert.Contains("<strong>6</strong> submitted in the last 30 days", html);
        Assert.Contains("Submit Request</a>", html);
    }

    [Fact]
    public async Task Read_CrossTenantAndMissingId_AreIndistinguishableAndHistoryIsOrdered()
    {
        var own = await RequestAsync(await TenancyAsync("accepted"));
        var other = await RequestAsync(await TenancyAsync("accepted", "MultiRoleAdmin"));
        await using (var db = _app.CreateContext())
        {
            db.MaintenanceStatusEvents.Add(new(MaintenanceRequestStatus.Closed, DateTime.UtcNow.AddMinutes(2), maintenanceRequestId: own));
            db.MaintenanceStatusEvents.Add(new(MaintenanceRequestStatus.InProgress, DateTime.UtcNow.AddMinutes(1), maintenanceRequestId: own));
            await db.SaveChangesAsync();
        }
        using var client = await ClientAsync(Actor.Tenant);
        using var detail = await client.GetAsync(Route + "/" + own);
        var result = await detail.Content.ReadFromJsonAsync<TenantMaintenanceDetail>();
        Assert.Equal(new[] { "InProgress", "Closed" }, result!.History.Select(e => e.Status));
        foreach (var id in new[] { other, int.MaxValue })
        {
            using var response = await client.GetAsync(Route + "/" + id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("resource_not_found", problem.GetProperty("code").GetString());
            using var razor = await client.GetAsync("/MaintenanceRequest/Details/" + id);
            Assert.Equal(HttpStatusCode.NotFound, razor.StatusCode);
        }
        using var list = await client.GetAsync(Route + "?pageSize=1");
        var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(own, page.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.Admin, 200)]
    public async Task RazorHistory_ParticipantAuthorization_ConcealsOtherOwners(Actor actor, int expected)
    {
        var id = await RequestAsync(await TenancyAsync("accepted"));
        using var client = await ClientAsync(actor);
        using var response = await client.GetAsync("/MaintenanceRequest/Details/" + id);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 200) Assert.Contains("Status history", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Submit_MultipleActiveTenancies_ReturnsConflictWithoutGuessing()
    {
        await TenancyAsync("accepted");
        await TenancyAsync("accepted");
        using var client = await ClientAsync(Actor.Tenant);
        using var result = await client.PostAsJsonAsync(Route, new { description = "A leaking kitchen sink" });
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Empty(await db.MaintenanceRequests.ToListAsync());
    }

    [Fact]
    public async Task Submit_EventInsertFailure_RollsBackRequestAndDoesNotLogContent()
    {
        await TenancyAsync("accepted");
        await using (var db = _app.CreateContext())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [MaintenanceStatusEvents] ADD CONSTRAINT [CK_Test_RejectEvents] CHECK ([Status] <> N'Submitted')");
        using var client = await ClientAsync(Actor.Tenant);
        using var response = await client.PostAsJsonAsync(Route, new { description = "Atomic write failure secret description" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verification = _app.CreateContext();
        Assert.Empty(await verification.MaintenanceRequests.ToListAsync());
        Assert.Empty(await verification.MaintenanceStatusEvents.ToListAsync());
        Assert.DoesNotContain("Atomic write failure secret description", string.Join('\n', _app.Logs));
    }

    [Fact]
    public async Task RazorSubmit_CookieAntiforgeryAndForgedTenancy_EnforcesSameInvariants()
    {
        var own = await TenancyAsync("accepted");
        var other = await TenancyAsync("accepted", "MultiRoleAdmin");
        using var client = await _app.CreateActorClientAsync(Actor.Tenant);
        var fields = new Dictionary<string, string> { ["TenancyId"] = own.ToString(), ["Description"] = "A leaking kitchen sink" };
        using var rejected = await client.PostAsync("/MaintenanceRequest/Create", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(client);
        fields["TenancyId"] = other.ToString();
        using var forged = await client.PostAsync("/MaintenanceRequest/Create", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        fields["TenancyId"] = own.ToString();
        fields["Status"] = "Closed";
        using var created = await client.PostAsync("/MaintenanceRequest/Create", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(MaintenanceRequestStatus.Submitted, (await db.MaintenanceRequests.SingleAsync()).Status);
        Assert.Single(await db.MaintenanceStatusEvents.ToListAsync());
    }

    [Fact]
    public async Task Migrate_ExistingRequests_BackfillsTruthfulBaselinesAndPreservesRows()
    {
        await using var db = _app.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016115250_TenancyInvitationLifecycle");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [Properties] ([Title],[Address],[RentAmount],[LandlordId]) VALUES (N'Existing',N'Address',1000,N'OwningLandlord')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [Tenancies] ([PropertyId],[TenantId],[StartDate],[Status]) VALUES (1,N'Tenant','2026-01-01',N'Active')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [MaintenanceRequests] ([Description],[TenancyId],[Status],[DateSubmitted],[LastUpdatedUtc]) VALUES (N'Existing submitted',1,N'Submitted','2026-01-01','2026-01-01'),(N'Existing resolved',1,N'Resolved','2026-01-01','2026-01-03')");
        await migrator.MigrateAsync();
        var history = await db.MaintenanceStatusEvents.OrderBy(e => e.MaintenanceRequestId).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, e => Assert.True(e.IsLegacyBaseline));
        Assert.Equal(MaintenanceRequestStatus.Resolved, history[1].Status);
        Assert.Equal(new DateTime(2026, 1, 3), history[1].OccurredUtc);
        Assert.Equal(2, await db.MaintenanceRequests.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        db.MaintenanceStatusEvents.Remove(history[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(9, 400)]
    [InlineData(10, 201)]
    [InlineData(2000, 201)]
    [InlineData(2001, 400)]
    public async Task Submit_DescriptionBounds_EnforcesModelLimit(int length, int expected)
    {
        await TenancyAsync("accepted");
        using var client = await ClientAsync(Actor.Tenant);
        using var result = await client.PostAsJsonAsync(Route, new { description = new string('x', length) });
        Assert.Equal(expected, (int)result.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(expected == 201 ? 1 : 0, await db.MaintenanceRequests.CountAsync());
        Assert.Equal(expected == 201 ? 1 : 0, await db.MaintenanceStatusEvents.CountAsync());
    }

    [Fact]
    public async Task Submit_RacesTenancyEnd_SerializesEligibilityAndInitialEvent()
    {
        var tenancy = await TenancyAsync("accepted");
        using var tenant = await ClientAsync(Actor.Tenant);
        using var owner = await ClientAsync(Actor.OwningLandlord);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Submit() { await gate.Task; return await tenant.PostAsJsonAsync(Route, new { description = "Concurrent kitchen leak" }); }
        async Task<HttpResponseMessage> End() { await gate.Task; return await owner.PostAsync("/api/v1/tenancies/" + tenancy + "/end", null); }
        var submitTask = Submit();
        var endTask = End();
        gate.SetResult();
        using var submitted = await submitTask;
        using var ended = await endTask;
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Contains(submitted.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        await using var db = _app.CreateContext();
        Assert.Equal(TenancyStatus.Ended, (await db.Tenancies.SingleAsync()).Status);
        var count = submitted.StatusCode == HttpStatusCode.Created ? 1 : 0;
        Assert.Equal(count, await db.MaintenanceRequests.CountAsync());
        Assert.Equal(count, await db.MaintenanceStatusEvents.CountAsync());
        if (count == 1)
            Assert.True((await db.MaintenanceRequests.SingleAsync()).DateSubmitted <= (await db.Tenancies.SingleAsync()).EndedUtc);
    }

    [Fact]
    public async Task ExistingRazorStatusCommand_AppendsEventAndRetainsImmutableHistory()
    {
        await TenancyAsync("accepted");
        using var tenant = await ClientAsync(Actor.Tenant);
        using var submitted = await tenant.PostAsJsonAsync(Route, new { description = "A leaking kitchen sink" });
        var receipt = await submitted.Content.ReadFromJsonAsync<MaintenanceSubmissionResponse>();
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var updated = await owner.PostAsync("/MaintenanceRequest/UpdateStatus/" + receipt!.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Id"] = receipt.Id.ToString(), ["NewStatus"] = "InProgress" }));
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(MaintenanceRequestStatus.InProgress, (await db.MaintenanceRequests.SingleAsync()).Status);
        var events = await db.MaintenanceStatusEvents.OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id).ToListAsync();
        Assert.Equal(new[] { MaintenanceRequestStatus.Submitted, MaintenanceRequestStatus.InProgress }, events.Select(e => e.Status));
        db.Entry(events[0]).Property(e => e.Status).CurrentValue = MaintenanceRequestStatus.Closed;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20261016115250_TenancyInvitationLifecycle"));
    }

    private async Task<HttpClient> ClientAsync(Actor actor)
    {
        var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        return client;
    }

    private async Task<int> TenancyAsync(string scenario, string tenant = "Tenant")
    {
        await using var db = _app.CreateContext();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var property = new Property { Title = "Home", Address = "Test Street", RentAmount = 1000, LandlordId = "OwningLandlord" };
        var tenancy = new Tenancy { Property = property, TenantId = tenant, StartDate = today.AddDays(-60),
            Status = scenario is "pending" or "expired-invitation" ? TenancyStatus.Pending
                : scenario is "ended" or "declined" ? TenancyStatus.Ended : TenancyStatus.Active,
            InvitedByUserId = "OwningLandlord", InvitedUtc = now.AddDays(-7),
            AcceptedUtc = scenario is "accepted" or "expired" or "ended" or "future" ? now.AddDays(-6) : null,
            InvitationExpiresUtc = scenario == "expired-invitation" ? now.AddDays(-1) : now.AddDays(1),
            InvitationTokenHash = scenario is "pending" or "expired-invitation" ? new string('A', 64) : null,
            EndReason = scenario == "declined" ? "Cancelled" : null };
        if (scenario == "expired") tenancy.EndDate = today.AddDays(-1);
        if (scenario == "future") tenancy.StartDate = today.AddDays(1);
        db.Tenancies.Add(tenancy);
        await db.SaveChangesAsync();
        return tenancy.Id;
    }

    private async Task<int> RequestAsync(int tenancy, MaintenanceRequestStatus status = MaintenanceRequestStatus.Submitted, DateTime? submitted = null)
    {
        await using var db = _app.CreateContext();
        var request = new MaintenanceRequest { TenancyId = tenancy, Description = "Existing kitchen leak", Status = status,
            DateSubmitted = submitted ?? DateTime.UtcNow, LastUpdatedUtc = submitted ?? DateTime.UtcNow };
        db.MaintenanceRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }
}
