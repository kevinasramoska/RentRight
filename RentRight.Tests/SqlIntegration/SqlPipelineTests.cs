using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Data;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class SqlPipelineTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app = new(server.ConnectionString);
    private const string Route = "/api/v1/properties";

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesEveryMigrationAndStartsWithNoProperties()
    {
        await using var context = _app.CreateContext();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Properties.ToListAsync());
        Assert.Equal(3, await context.Roles.CountAsync());
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 204)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.MultiRoleAdmin, 204)]
    [InlineData(Actor.Admin, 204)]
    public async Task Update_ActorMatrix_EnforcesCookieRolesAndConcealsOwnership(Actor actor, int expected)
    {
        var id = await SeedPropertyAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PutAsJsonAsync($"{Route}/{id}", Values("Changed", await PropertyVersionAsync(id)));
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected != 204)
        {
            await AssertProblemAsync(response, expected, expected switch
            {
                401 => "authentication_required", 403 => "access_denied", _ => "resource_not_found"
            });
            Assert.Null(response.Headers.Location);
        }
        await using var context = _app.CreateContext();
        var property = await context.Properties.SingleAsync();
        Assert.Equal(expected == 204 ? "Changed" : "Original", property.Title);
        Assert.Equal(Actor.OwningLandlord.ToString(), property.LandlordId);
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 201)]
    [InlineData(Actor.OtherLandlord, 201)]
    [InlineData(Actor.MultiRoleAdmin, 201)]
    [InlineData(Actor.Admin, 201)]
    public async Task Create_ActorMatrix_UsesPersistedIdentityAsOwner(Actor actor, int expected)
    {
        using var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(Route, Values("Created"));
        Assert.Equal(expected, (int)response.StatusCode);
        await using var context = _app.CreateContext();
        if (expected != 201)
        {
            await AssertProblemAsync(response, expected, expected == 401 ? "authentication_required" : "access_denied");
            Assert.Empty(await context.Properties.ToListAsync());
            return;
        }
        var property = await context.Properties.SingleAsync();
        Assert.Equal(actor.ToString(), property.LandlordId);
        Assert.Equal("Created", property.Title);
        Assert.Equal($"{Route}/{property.Id}", response.Headers.Location?.AbsolutePath);
        using var publicClient = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var get = await publicClient.GetAsync(response.Headers.Location);
        get.EnsureSuccessStatusCode();
        var json = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain("landlordId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"rentAmount\":1250", json);
    }

    [Theory]
    [InlineData(Actor.Anonymous, 401)]
    [InlineData(Actor.Tenant, 403)]
    [InlineData(Actor.OwningLandlord, 204)]
    [InlineData(Actor.OtherLandlord, 404)]
    [InlineData(Actor.MultiRoleAdmin, 204)]
    [InlineData(Actor.Admin, 204)]
    public async Task Delete_ActorMatrix_EnforcesOwnership(Actor actor, int expected)
    {
        var id = await SeedPropertyAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        client.DefaultRequestHeaders.Add("X-Expected-Version", await PropertyVersionAsync(id));
        using var response = await client.DeleteAsync($"{Route}/{id}");
        Assert.Equal(expected, (int)response.StatusCode);
        await using var context = _app.CreateContext();
        Assert.Equal(expected != 204, await context.Properties.AnyAsync());
    }

    [Fact]
    public async Task Create_RealLoginCookie_RequiresMatchingAntiforgeryAndValidBody()
    {
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var missing = await client.PostAsJsonAsync(Route, Values());
        await AssertProblemAsync(missing, 400, "invalid_request");
        await SqlApplication.AddAntiforgeryAsync(client);
        using var other = await _app.CreateActorClientAsync(Actor.OtherLandlord);
        await SqlApplication.AddAntiforgeryAsync(other);
        using var forged = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(Values()) };
        forged.Headers.Add("X-CSRF-TOKEN", other.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN"));
        using var rejected = await client.SendAsync(forged);
        await AssertProblemAsync(rejected, 400, "invalid_request");
        using var invalid = await client.PostAsJsonAsync(Route, new { title = "", address = "", rentAmount = -1 });
        await AssertProblemAsync(invalid, 400, "validation_failed");
        using var accepted = await client.PostAsJsonAsync(Route, Values());
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        await using var context = _app.CreateContext();
        Assert.Single(await context.Properties.ToListAsync());
    }

    [Fact]
    public async Task Delete_TenancyHistory_ReturnsConflictAndMissingIdReturnsNotFound()
    {
        var id = await SeedPropertyAsync();
        await using (var context = _app.CreateContext())
        {
            context.Tenancies.Add(Tenancy(id, TenancyStatus.Ended));
            await context.SaveChangesAsync();
        }
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        client.DefaultRequestHeaders.Add("X-Expected-Version", await PropertyVersionAsync(id));
        using var conflict = await client.DeleteAsync($"{Route}/{id}");
        await AssertProblemAsync(conflict, 409, "resource_conflict");
        using var missing = await client.PutAsJsonAsync($"{Route}/2147483647", Values());
        await AssertProblemAsync(missing, 404, "resource_not_found");
        await using var persisted = _app.CreateContext();
        Assert.True(await persisted.Properties.AnyAsync(property => property.Id == id));
        Assert.Single(await persisted.Tenancies.ToListAsync());
    }

    [Fact]
    public async Task List_Anonymous_ExecutesSqlFilteringOrderingAndPagingWithoutPrivateData()
    {
        await SeedPropertyAsync("Zeta");
        await SeedPropertyAsync("Alpha");
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var response = await client.GetAsync(Route + "?minimumRent=1000&maximumRent=1500&pageSize=1");
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha", Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
        Assert.DoesNotContain("landlordId", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("foreign-key", 547)]
    [InlineData("date-check", 547)]
    [InlineData("filtered-unique", 2601)]
    public async Task Save_InvalidRelationalState_SqlServerRejectsConstraint(string scenario, int sqlError)
    {
        var id = await SeedPropertyAsync();
        await using var context = _app.CreateContext();
        if (scenario == "foreign-key")
        {
            context.Properties.Add(new Property { Title = "Invalid", Address = "Address", LandlordId = "missing-user" });
        }
        else
        {
            // Ended history can coexist; only duplicate Active rows are prohibited.
            context.Tenancies.Add(Tenancy(id, TenancyStatus.Ended));
            context.Tenancies.Add(Tenancy(id, TenancyStatus.Active));
            await context.SaveChangesAsync();
            var invalid = Tenancy(id, scenario == "date-check" ? TenancyStatus.Ended : TenancyStatus.Active);
            if (scenario == "date-check") invalid.EndDate = invalid.StartDate.AddDays(-1);
            context.Tenancies.Add(invalid);
        }
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(sqlError, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Fact]
    public async Task UpdateRoles_LastAdministrator_RetainsRealIdentityAssignment()
    {
        // Leave exactly one administrator, then attempt to remove it through MVC.
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var multi = await users.FindByIdAsync(Actor.MultiRoleAdmin.ToString());
            Assert.True((await users.RemoveFromRoleAsync(multi!, "Admin")).Succeeded);
        }
        using var client = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsync("/Admin/UpdateRoles", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["userId"] = Actor.Admin.ToString(), ["selectedRoles"] = "Tenant" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var page = await client.GetAsync("/Admin");
        page.EnsureSuccessStatusCode();
        Assert.Contains("At least one administrator account must remain", await page.Content.ReadAsStringAsync());
        using var verificationScope = _app.Services.CreateScope();
        var manager = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await manager.FindByIdAsync(Actor.Admin.ToString());
        Assert.Equal(new[] { "Admin" }, await manager.GetRolesAsync(admin!));
    }

    [Theory]
    [InlineData(Actor.Anonymous, 302, "/Identity/Account/Login")]
    [InlineData(Actor.Tenant, 302, "/Identity/Account/AccessDenied")]
    [InlineData(Actor.OwningLandlord, 302, "/Identity/Account/AccessDenied")]
    [InlineData(Actor.OtherLandlord, 302, "/Identity/Account/AccessDenied")]
    [InlineData(Actor.Admin, 200, null)]
    [InlineData(Actor.MultiRoleAdmin, 200, null)]
    public async Task Admin_NamedRolePolicy_EnforcesCookieActorMatrix(Actor actor, int status, string? redirectPath)
    {
        using var client = await _app.CreateActorClientAsync(actor);

        using var response = await client.GetAsync("/Admin");

        Assert.Equal(status, (int)response.StatusCode);
        if (redirectPath is not null)
            Assert.Equal(redirectPath, new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        else
            Assert.Null(response.Headers.Location);
    }

    private async Task<string> PropertyVersionAsync(int id)
    {
        await using var db = _app.CreateContext();
        return Convert.ToBase64String((await db.Properties.SingleAsync(p => p.Id == id)).RowVersion);
    }

    private async Task<int> SeedPropertyAsync(string title = "Original")
    {
        await using var context = _app.CreateContext();
        var property = new Property
        {
            Title = title, Address = "1 Test Street", RentAmount = 1250m,
            LandlordId = Actor.OwningLandlord.ToString()
        };
        context.Properties.Add(property);
        await context.SaveChangesAsync();
        return property.Id;
    }

    private static Tenancy Tenancy(int propertyId, TenancyStatus status) => new()
    {
        PropertyId = propertyId, TenantId = Actor.Tenant.ToString(),
        StartDate = new DateOnly(2026, 1, 1), Status = status,
        EndDate = status == TenancyStatus.Ended ? new DateOnly(2026, 2, 1) : null
    };

    private static object Values(string title = "Apartment", string? expectedVersion = null) => new
    {
        expectedVersion, title, address = "1 Test Street", rentAmount = 1250m, description = "Test",
        landlordId = Actor.OtherLandlord.ToString(), id = int.MaxValue
    };

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
