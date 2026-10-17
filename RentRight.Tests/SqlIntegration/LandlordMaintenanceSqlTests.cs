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
public sealed class LandlordMaintenanceSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    private const string Route = "/api/v1/landlord-maintenance-requests";
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.Admin, 200)]
    [InlineData(Actor.MultiRoleAdmin, 200)]
    public async Task Detail_EndedTenancy_EnforcesOwnerAndAdminMatrix(Actor actor, int expected)
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        using var client = await ClientAsync(actor);
        using var detail = await client.GetAsync(Route + "/" + id);
        Assert.Equal(expected, (int)detail.StatusCode);
        if (expected == 200)
        {
            using var updated = await client.PostAsJsonAsync(Route + "/" + id + "/triage",
                new { expectedRevision = 0, newStatus = "InProgress", priority = "High", note = "Owner triaged" });
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            await using var db = _app.CreateContext();
            Assert.Equal(actor.ToString(), (await db.MaintenanceStatusEvents.SingleAsync()).ActorUserId);
        }
        if (actor == Actor.OtherLandlord)
        {
            using var missing = await client.GetAsync(Route + "/2147483647");
            Assert.Equal(detail.StatusCode, missing.StatusCode);
            using var denied = await client.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 0, newStatus = "InProgress" });
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var list = await client.GetAsync(Route);
            Assert.Equal(0, JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("totalCount").GetInt32());
        }
    }

    [Fact]
    public async Task Queue_FiltersAndPaging_AreStableBoundedAndScoped()
    {
        var tenancy = await TenancyAsync("ended");
        var time = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var ids = new List<int>();
        for (var i = 0; i < 5; i++) ids.Add(await RequestAsync(tenancy, submitted: time));
        await RequestAsync(tenancy, MaintenanceRequestStatus.Closed, time.AddDays(-1));
        await using var db = _app.CreateContext();
        var property = (await db.Tenancies.SingleAsync()).PropertyId;
        using var owner = await ClientAsync(Actor.OwningLandlord);
        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            using var response = await owner.GetAsync(Route + $"?page={page}&pageSize=2&status=Submitted&propertyId={property}&submittedFrom=2026-10-01&submittedTo=2026-10-01");
            response.EnsureSuccessStatusCode();
            var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(5, data.GetProperty("totalCount").GetInt32());
            seen.AddRange(data.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        }
        Assert.Equal(ids.AsEnumerable().Reverse(), seen);
        foreach (var filter in new[] { "page=0", "page=10001", "pageSize=101", "propertyId=0", "status=999", "status=Bad", "submittedFrom=2026-10-02&submittedTo=2026-10-01", "submittedFrom=bad" })
        {
            using var invalid = await owner.GetAsync(Route + "?" + filter);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var empty = await owner.GetAsync("/MaintenanceQueue?PropertyId=2147483647");
        Assert.Contains("No maintenance requests match", await empty.Content.ReadAsStringAsync());
        using var beyond = await owner.GetAsync(Route + "?page=10000");
        Assert.Empty(JsonDocument.Parse(await beyond.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Update_StatusAndPriority_AuditsServerFieldsEncodesNotesAndRejectsStaleForm()
    {
        var id = await RequestAsync(await TenancyAsync("accepted"));
        using var owner = await ClientAsync(Actor.OwningLandlord);
        var before = DateTime.UtcNow;
        const string note = "<script>secretAuditNote()</script>";
        using var updated = await owner.PostAsJsonAsync(Route + "/" + id + "/triage",
            new { expectedRevision = 0, newStatus = "InProgress", priority = "Urgent", note,
                actorUserId = "OtherLandlord", occurredUtc = "2000-01-01", tenancyId = 999 });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        using var stale = await owner.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 0, priority = "Low" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var razor = await owner.GetAsync("/MaintenanceRequest/Details/" + id);
        var html = await razor.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain(note, html);
        using var staleForm = await owner.PostAsync("/MaintenanceRequest/UpdateStatus/" + id,
            new FormUrlEncodedContent(new Dictionary<string,string> { ["Id"] = id.ToString(), ["ExpectedRevision"] = "0", ["Priority"] = "Low" }));
        Assert.Equal(HttpStatusCode.Conflict, staleForm.StatusCode);
        using var tenant = await ClientAsync(Actor.Tenant);
        using var detail = await tenant.GetAsync("/api/v1/maintenance-requests/" + id);
        Assert.Contains("secretAuditNote", await detail.Content.ReadAsStringAsync());
        await using var db = _app.CreateContext();
        var request = await db.MaintenanceRequests.SingleAsync();
        Assert.Equal(1, request.Revision);
        Assert.Equal(MaintenancePriority.Urgent, request.Priority);
        var entry = await db.MaintenanceStatusEvents.SingleAsync();
        Assert.Equal(MaintenanceRequestStatus.Submitted, entry.PreviousStatus);
        Assert.Equal(MaintenanceRequestStatus.InProgress, entry.Status);
        Assert.Equal("OwningLandlord", entry.ActorUserId);
        Assert.Equal(note, entry.Note);
        Assert.InRange(entry.OccurredUtc, before, DateTime.UtcNow);
        Assert.DoesNotContain(note, string.Join('\n', _app.Logs));
    }

    [Fact]
    public async Task Update_CompetingRequests_OnlyOneWinsAndPriorityOnlyDoesNotInventTransition()
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        using var first = await ClientAsync(Actor.OwningLandlord);
        using var second = await ClientAsync(Actor.Admin);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 0, newStatus = "InProgress", priority = "High" }),
            second.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 0, newStatus = "InProgress", priority = "Urgent" }));
        Assert.Equal(new[] { 204, 409 }, responses.Select(x => (int)x.StatusCode).Order());
        foreach (var response in responses) response.Dispose();
        await using (var db = _app.CreateContext())
        {
            Assert.Single(await db.MaintenanceStatusEvents.ToListAsync());
            Assert.Equal(1, (await db.MaintenanceRequests.SingleAsync()).Revision);
        }
        using var priority = await first.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 1, priority = "Low" });
        Assert.Equal(HttpStatusCode.NoContent, priority.StatusCode);
        await using var verify = _app.CreateContext();
        Assert.Single(await verify.MaintenanceStatusEvents.ToListAsync());
        Assert.Equal(2, (await verify.MaintenanceRequests.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Update_EventFailure_RollsBackStatusPriorityRevisionAndHistory()
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        byte[] versionBefore;
        await using (var db = _app.CreateContext())
        {
            versionBefore = (await db.MaintenanceRequests.SingleAsync()).RowVersion;
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [MaintenanceStatusEvents] ADD CONSTRAINT [CK_Test_RejectEvents] CHECK ([Status] <> N'InProgress')");
        }
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var response = await owner.PostAsJsonAsync(Route + "/" + id + "/triage",
            new { expectedRevision = 0, newStatus = "InProgress", priority = "Urgent", note = "Secret failed note" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = _app.CreateContext();
        var request = await verify.MaintenanceRequests.SingleAsync();
        Assert.Equal(MaintenanceRequestStatus.Submitted, request.Status);
        Assert.Equal(MaintenancePriority.Normal, request.Priority);
        Assert.Equal(0, request.Revision);
        Assert.Equal(versionBefore, request.RowVersion);
        Assert.Empty(await verify.MaintenanceStatusEvents.ToListAsync());
        Assert.DoesNotContain("Secret failed note", string.Join('\n', _app.Logs));
    }

    [Theory]
    [InlineData("{\"expectedRevision\":0,\"newStatus\":999}")]
    [InlineData("{\"expectedRevision\":0,\"priority\":999}")]
    [InlineData("{\"expectedRevision\":0,\"priority\":\"Invalid\"}")]
    [InlineData("{\"priority\":\"High\"}")]
    [InlineData("{\"expectedRevision\":0,\"newStatus\":\"Closed\",\"priority\":\"High\"}")]
    public async Task Update_InvalidInput_ChangesNothing(string json)
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var response = await owner.PostAsync(Route + "/" + id + "/triage", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(0, (await db.MaintenanceRequests.SingleAsync()).Revision);
        Assert.Empty(await db.MaintenanceStatusEvents.ToListAsync());
    }

    [Fact]
    public async Task Update_WithoutAntiforgery_RejectsApiAndRazor()
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        using var owner = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var api = await owner.PostAsJsonAsync(Route + "/" + id + "/triage", new { expectedRevision = 0, newStatus = "InProgress" });
        Assert.Equal(HttpStatusCode.BadRequest, api.StatusCode);
        using var razor = await owner.PostAsync("/MaintenanceRequest/UpdateStatus/" + id,
            new FormUrlEncodedContent(new Dictionary<string,string> { ["Id"] = id.ToString(), ["ExpectedRevision"] = "0", ["NewStatus"] = "InProgress" }));
        Assert.Equal(HttpStatusCode.BadRequest, razor.StatusCode);
    }
    [Fact]
    public async Task Update_CompleteTransitionTable_AtomicallyAcceptsOnlyAllowedPairs()
    {
        var tenancy = await TenancyAsync("ended");
        using var owner = await ClientAsync(Actor.OwningLandlord);
        foreach (var current in Enum.GetValues<MaintenanceRequestStatus>())
        foreach (var next in Enum.GetValues<MaintenanceRequestStatus>())
        {
            var id = await RequestAsync(tenancy, current);
            var allowed = (current, next) is (MaintenanceRequestStatus.Submitted, MaintenanceRequestStatus.InProgress)
                or (MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Resolved)
                or (MaintenanceRequestStatus.Resolved, MaintenanceRequestStatus.InProgress)
                or (MaintenanceRequestStatus.Resolved, MaintenanceRequestStatus.Closed);
            using var response = await owner.PostAsJsonAsync(Route + "/" + id + "/triage",
                new { expectedRevision = 0, newStatus = next.ToString(), priority = current == next ? "Normal" : "High" });
            Assert.Equal(allowed ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest, response.StatusCode);
            await using var db = _app.CreateContext();
            var row = await db.MaintenanceRequests.SingleAsync(r => r.Id == id);
            Assert.Equal(allowed ? next : current, row.Status);
            Assert.Equal(allowed ? MaintenancePriority.High : MaintenancePriority.Normal, row.Priority);
            Assert.Equal(allowed ? 1 : 0, await db.MaintenanceStatusEvents.CountAsync(e => e.MaintenanceRequestId == id));
        }
    }

    [Fact]
    public async Task Update_NoteBoundsAndReopen_OrderEventsAndPreserveLegacyUnknowns()
    {
        var id = await RequestAsync(await TenancyAsync("ended"));
        using var owner = await ClientAsync(Actor.OwningLandlord);
        using var oversized = await owner.PostAsJsonAsync(Route + "/" + id + "/triage",
            new { expectedRevision = 0, newStatus = "InProgress", note = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        var sequence = new[] { "InProgress", "Resolved", "InProgress", "Resolved", "Closed" };
        for (var revision = 0; revision < sequence.Length; revision++)
        {
            using var response = await owner.PostAsJsonAsync(Route + "/" + id + "/triage",
                new { expectedRevision = revision, newStatus = sequence[revision], note = revision == 0 ? new string('x', 1000) : "" });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using var detail = await owner.GetAsync(Route + "/" + id);
        var result = await detail.Content.ReadFromJsonAsync<MaintenanceTriageDetail>();
        Assert.Equal(sequence, result!.History.Select(e => e.Status));
        Assert.Equal(1000, result.History[0].Note!.Length);
        Assert.All(result.History.Skip(1), e => Assert.Null(e.Note));
        Assert.Equal(result.History.OrderBy(e => e.OccurredUtc), result.History);
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
            Status = scenario is "pending" or "expired-invitation" or "unaccepted" ? TenancyStatus.Pending
                : scenario is "ended" or "declined" ? TenancyStatus.Ended : TenancyStatus.Active,
            InvitedByUserId = "OwningLandlord", InvitedUtc = now.AddDays(-7),
            AcceptedByUserId = scenario is "accepted" or "expired" or "ended" or "future" ? tenant : null,
            AcceptedUtc = scenario is "accepted" or "expired" or "ended" or "future" ? now.AddDays(-6) : null,
            InvitationExpiresUtc = scenario == "expired-invitation" ? now.AddDays(-1) : now.AddDays(1),
            InvitationTokenHash = scenario is "pending" or "expired-invitation" or "unaccepted" ? new string('A', 64) : null,
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
