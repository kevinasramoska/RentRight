using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class WorkspaceSqlTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.Admin, 200)]
    [InlineData(Actor.MultiRoleAdmin, 200)]
    public async Task Dashboard_RealRoleMatrix_ReturnsOnlyScopedSummary(Actor actor, int status)
    {
        await SeedAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        using var response = await client.GetAsync("/api/v1/workspace");
        Assert.Equal(status, (int)response.StatusCode);
        if (status != 200) return;
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
        if (actor == Actor.OwningLandlord)
        {
            Assert.Contains("Owned home", body);
            Assert.DoesNotContain("Other home", body);
        }
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.OwningLandlord, 200)]
    [InlineData(Actor.Admin, 200)]
    [InlineData(Actor.MultiRoleAdmin, 200)]
    public async Task Property_ResourceMatrix_ConcealsCrossOwnerAndReturnsVersion(Actor actor, int status)
    {
        var id = await SeedAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        using var response = await client.GetAsync($"/api/v1/workspace/properties/{id}");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
        if (status == 200)
        {
            using var json = JsonDocument.Parse(body);
            Assert.True(json.RootElement.GetProperty("canManage").GetBoolean());
            Assert.Equal(8, Convert.FromBase64String(json.RootElement.GetProperty("version").GetString()!).Length);
        }
        if (status == 404) Assert.DoesNotContain("Owned home", body);
    }

    [Fact]
    public async Task PublishedProperty_Anonymous_ReturnsLocationWithoutManagerMetadata()
    {
        var id = await SeedAsync();
        await using (var db = _app.CreateContext())
        {
            var property = await db.Properties.SingleAsync(property => property.Id == id);
            property.Latitude = 53.3m; property.Longitude = -6.2m;
            property.GeocodingPlaceId = "private-provider-reference"; property.GeocodedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var response = await client.GetAsync($"/api/v1/workspace/published-properties/{id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(53.3m, json.RootElement.GetProperty("location").GetProperty("latitude").GetDecimal());
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(json.RootElement.GetProperty("canManage").GetBoolean());
        Assert.DoesNotContain("Landlord", body);
        Assert.DoesNotContain("private-provider-reference", body);
    }

    [Fact]
    public async Task ManagerTenancies_OnlyOwnerOrAdmin_CanReadTenantContactProjection()
    {
        var id = await SeedAsync();
        await using (var db = _app.CreateContext())
        {
            db.Tenancies.Add(new Tenancy { PropertyId = id, TenantId = Actor.Tenant.ToString(), StartDate = new(2020, 1, 1), Status = TenancyStatus.Active });
            await db.SaveChangesAsync();
        }
        using var owner = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var ownerPage = await owner.GetAsync("/api/v1/workspace/tenancies");
        ownerPage.EnsureSuccessStatusCode();
        Assert.Contains("Tenant@example.test", await ownerPage.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await ownerPage.Content.ReadAsStringAsync());
        var row = json.RootElement.GetProperty("items")[0];
        Assert.Equal("Active", row.GetProperty("status").GetString());
        Assert.Equal(8, Convert.FromBase64String(row.GetProperty("version").GetString()!).Length);
        using var other = await _app.CreateActorClientAsync(Actor.OtherLandlord);
        using var hidden = await other.GetAsync("/api/v1/workspace/tenancies");
        hidden.EnsureSuccessStatusCode();
        Assert.DoesNotContain("Tenant@example.test", await hidden.Content.ReadAsStringAsync());
        using var tenant = await _app.CreateActorClientAsync(Actor.Tenant);
        using var rejected = await tenant.GetAsync("/api/v1/workspace/tenancies");
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task InvitationForm_OnlyOwnedAvailableProperties_AreProjected()
    {
        await SeedAsync();
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var response = await client.GetAsync("/api/v1/workspace/invitation-form");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Owned home", body);
        Assert.DoesNotContain("Other home", body);
        Assert.DoesNotContain("Tenant@example.test", body);
    }

    [Theory]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.OwningLandlord, 503)]
    public async Task LocationRefresh_ValidAntiforgery_ConcealsOtherOwnersAndKeepsDisabledProviderOff(Actor actor, int status)
    {
        var id = await SeedAsync();
        string version;
        await using (var db = _app.CreateContext()) version = Convert.ToBase64String((await db.Properties.SingleAsync(property => property.Id == id)).RowVersion);
        using var client = await _app.CreateActorClientAsync(actor);
        using var blocked = await client.PostAsJsonAsync($"/api/v1/workspace/properties/{id}/location", new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync($"/api/v1/workspace/properties/{id}/location", new { expectedVersion = version, address = "forged", latitude = 90, longitude = 180 });
        Assert.Equal(status, (int)response.StatusCode);
        await using var verify = _app.CreateContext();
        var property = await verify.Properties.SingleAsync(property => property.Id == id);
        Assert.Null(property.Latitude);
        Assert.Equal("Synthetic street", property.Address);
    }

    [Fact]
    public async Task AdminRoles_RealService_RejectsLastUsableAdministratorAndUnknownRoles()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var invalid = await client.PutAsJsonAsync("/api/v1/admin-users/roles", new { userId = Actor.Tenant.ToString(), selectedRoles = new[] { "SuperUser" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var first = await client.PutAsJsonAsync("/api/v1/admin-users/roles", new { userId = Actor.MultiRoleAdmin.ToString(), selectedRoles = new[] { "Tenant" } });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var last = await client.PutAsJsonAsync("/api/v1/admin-users/roles", new { userId = Actor.Admin.ToString(), selectedRoles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        using var json = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        Assert.Equal("last_administrator", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_RealCookie_IsRevokedAndPrivateReadsStop()
    {
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var logout = await client.PostAsJsonAsync("/api/v1/session/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var session = await client.GetAsync("/api/v1/session");
        using var json = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isAuthenticated").GetBoolean());
        using var denied = await client.GetAsync("/api/v1/workspace");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private async Task<int> SeedAsync()
    {
        await using var db = _app.CreateContext();
        var owned = new Property { Title = "Owned home", Address = "Synthetic street", RentAmount = 1000, LandlordId = Actor.OwningLandlord.ToString() };
        db.Properties.AddRange(owned, new Property { Title = "Other home", Address = "Other synthetic street", RentAmount = 1200, LandlordId = Actor.OtherLandlord.ToString() });
        await db.SaveChangesAsync();
        return owned.Id;
    }
}
