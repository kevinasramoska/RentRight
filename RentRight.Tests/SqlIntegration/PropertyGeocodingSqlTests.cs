using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using RentRight.Features.Properties;
using RentRight.Integrations.Geocoding;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class PropertyGeocodingSqlTests : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app;
    private readonly FakeGeocoding _geo = new();
    private readonly SqlServerFixture _server;

    public PropertyGeocodingSqlTests(SqlServerFixture server)
    {
        _server = server;
        _app = new(server.ConnectionString)
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGeocodingClient>();
                services.AddSingleton<IGeocodingClient>(_geo);
            }
        };
    }

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Anonymous, 302, 0)]
    [InlineData(Actor.Tenant, 302, 0)]
    [InlineData(Actor.OtherLandlord, 404, 0)]
    [InlineData(Actor.OwningLandlord, 302, 1)]
    [InlineData(Actor.Admin, 302, 1)]
    [InlineData(Actor.MultiRoleAdmin, 302, 1)]
    public async Task Refresh_RealCookiesAndActorMatrix_PreventsCrossUserLookup(Actor actor, int status, int calls)
    {
        var id = await SeedAsync();
        using var client = await _app.CreateActorClientAsync(actor);
        if (actor != Actor.Anonymous) await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form(await VersionAsync(id)));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(calls, _geo.Calls);
        await using var db = _app.CreateContext();
        Assert.Equal(calls == 1, (await db.Properties.SingleAsync()).Latitude is not null);
    }

    [Fact]
    public async Task Refresh_MissingAntiforgeryOrBadVersions_CannotCallProvider()
    {
        var id = await SeedAsync();
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var rejected = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form(await VersionAsync(id)));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var invalid = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form("forged"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var stale = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form("AAAAAAAAAAE="));
        Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
        using var missing = await client.PostAsync("/PropertyLocations/Refresh/2147483647", Form(await VersionAsync(id)));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, _geo.Calls);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("owner")]
    [InlineData("other-edit")]
    public async Task Refresh_ConcurrentPropertyChange_NeverWritesStaleLookup(string change)
    {
        var id = await SeedAsync();
        _geo.BeforeReturn = async () =>
        {
            await using var db = _app.CreateContext();
            var property = await db.Properties.SingleAsync();
            if (change == "address") property.Address = "New synthetic address";
            else if (change == "owner") property.LandlordId = Actor.OtherLandlord.ToString();
            else property.Title = "Concurrent edit";
            await db.SaveChangesAsync();
        };
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form(await VersionAsync(id)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var verify = _app.CreateContext();
        Assert.Null((await verify.Properties.SingleAsync()).Latitude);
        Assert.Equal(1, _geo.Calls);
    }

    [Theory]
    [InlineData("same", false)]
    [InlineData("different", true)]
    [InlineData("trailing-space", true)]
    [InlineData("case", true)]
    public async Task Update_AddressChangesClearAllLocationFactsAtomically_EvenOnProviderOutage(string change, bool cleared)
    {
        var id = await SeedAsync(located: true);
        _geo.Result = new(GeocodingStatus.Unavailable);
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        var address = change switch
        {
            "different" => "Different synthetic address", "trailing-space" => "Synthetic Street ",
            "case" => "synthetic street", _ => "Synthetic Street"
        };
        using var response = await client.PutAsJsonAsync($"/api/v1/properties/{id}", new
        { title = "Edited title", address, rentAmount = 1250, expectedVersion = await VersionAsync(id) });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = _app.CreateContext();
        var property = await db.Properties.SingleAsync();
        Assert.Equal(address, property.Address);
        Assert.Equal(!cleared, property.Latitude is not null);
        Assert.Equal(!cleared, property.Longitude is not null);
        Assert.Equal(!cleared, property.GeocodingPlaceId is not null);
        Assert.Equal(!cleared, property.GeocodedUtc is not null);
        Assert.Equal(cleared ? 1 : 0, _geo.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithSuccessfulLookupOrProviderFailure_StillCommitsCoreProperty(bool failure)
    {
        if (failure) _geo.Result = new(GeocodingStatus.Unavailable);
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/v1/properties", new
        {
            title = "Synthetic", address = "Synthetic Street", rentAmount = 1250,
            latitude = 90, longitude = 180, geocodingPlaceId = "forged", geocodedUtc = "1900-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = _app.CreateContext();
        var property = await db.Properties.SingleAsync();
        Assert.Equal(Actor.OwningLandlord.ToString(), property.LandlordId);
        Assert.Equal(failure ? null : 53.349805m, property.Latitude);
        Assert.NotEqual("forged", property.GeocodingPlaceId);
        Assert.Equal(1, _geo.Calls);

        using var page = await client.GetAsync($"/Properties/Details/{property.Id}");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(!failure, html.Contains("Approximate location"));
        Assert.Equal(!failure, html.Contains("https://www.openstreetmap.org/?mlat="));
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("synthetic-place", html);
        Assert.DoesNotContain("apiKey", html);
        using var publicClient = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var publicPage = await publicClient.GetAsync($"/Properties/Details/{property.Id}");
        Assert.DoesNotContain("Find or refresh map location", await publicPage.Content.ReadAsStringAsync());
        Assert.Equal(1, _geo.Calls); // Public reads never trigger a lookup.
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("latitude")]
    [InlineData("longitude")]
    [InlineData("empty-reference")]
    public async Task Save_InvalidLocationFacts_SqlConstraintRejects(string scenario)
    {
        var id = await SeedAsync(located: true);
        await using var db = _app.CreateContext();
        var property = await db.Properties.SingleAsync(property => property.Id == id);
        switch (scenario)
        {
            case "partial": property.Longitude = null; break;
            case "latitude": property.Latitude = 91; break;
            case "longitude": property.Longitude = -181; break;
            case "empty-reference": property.GeocodingPlaceId = ""; break;
        }
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Fact]
    public async Task Create_RealAdapterWithFakeHttp_ParsesPersistsAndDoesNotLogQuerySecrets()
    {
        await using var app = new SqlApplication(_server.ConnectionString)
        {
            ConfigureTestServices = services =>
            {
                services.Configure<GeocodingOptions>(options =>
                { options.Enabled = true; options.ApiKey = "test-only-provider-key"; });
                // Override every factory-created transport, after named configuration.
                // This test cannot accidentally contact any live provider.
                services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = new FakeHttp()));
            }
        };
        await app.InitializeAsync();
        using var client = await app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var createPage = await client.GetAsync("/Properties/Create");
        Assert.Contains("addresses are sent to Geoapify", await createPage.Content.ReadAsStringAsync());
        using var response = await client.PostAsJsonAsync("/api/v1/properties", new
        { title = "Synthetic", address = "Synthetic Street", rentAmount = 1200 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = app.CreateContext();
        var property = await db.Properties.SingleAsync();
        Assert.Equal(53.349805m, property.Latitude);
        Assert.Equal("synthetic-provider-place", property.GeocodingPlaceId);
        using var page = await client.GetAsync($"/Properties/Details/{property.Id}");
        Assert.Contains("Approximate location", await page.Content.ReadAsStringAsync());
        Assert.DoesNotContain(app.Logs, line => line.Contains("test-only-provider-key", StringComparison.Ordinal) ||
            line.Contains("api.geoapify.com/v1/geocode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_LongProviderReference_PersistsWithoutTruncation()
    {
        var id = await SeedAsync();
        var placeId = new string('x', 2048);
        _geo.Result = new(GeocodingStatus.Found, new(53.349805m, -6.26031m, placeId));
        using var client = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsync($"/PropertyLocations/Refresh/{id}", Form(await VersionAsync(id)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Equal(placeId, (await db.Properties.SingleAsync()).GeocodingPlaceId);
    }

    [Fact]
    public async Task Migration_ExistingProperty_UpDownAndReapplyPreserveAddressAndOwnership()
    {
        await using var legacy = new SqlApplication(_server.ConnectionString);
        const string previous = "20261017134027_IntegrityConcurrencyRetention";
        await legacy.InitializeAsync(previous);
        await using var db = legacy.CreateContext();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Properties (Title, Address, RentAmount, LandlordId) VALUES (N'Synthetic', N'Existing address', 1000, N'OwningLandlord')");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261018071947_OptionalPropertyGeocoding");
        Assert.Null((await db.Properties.AsNoTracking().SingleAsync()).Latitude);
        var originalReference = new string('x', 512);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Properties SET Latitude = 53.3, Longitude = -6.2, GeocodedUtc = SYSUTCDATETIME(), GeocodingPlaceId = {originalReference}");
        await migrator.MigrateAsync();
        var property = await db.Properties.AsNoTracking().SingleAsync();
        Assert.Equal(53.3m, property.Latitude);
        Assert.Equal(originalReference, property.GeocodingPlaceId);
        Assert.Equal("Existing address", property.Address);
        Assert.Equal("OwningLandlord", property.LandlordId);
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        Assert.Equal("Existing address", (await db.Properties.AsNoTracking().SingleAsync()).Address);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private async Task<int> SeedAsync(bool located = false)
    {
        await using var db = _app.CreateContext();
        var property = new Property { Title = "Synthetic", Address = "Synthetic Street", RentAmount = 1000,
            LandlordId = Actor.OwningLandlord.ToString(), Latitude = located ? 53.3m : null,
            Longitude = located ? -6.2m : null, GeocodingPlaceId = located ? "existing-synthetic" : null,
            GeocodedUtc = located ? DateTime.UtcNow : null };
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }
    private async Task<string> VersionAsync(int id)
    {
        await using var db = _app.CreateContext();
        return Convert.ToBase64String((await db.Properties.SingleAsync(property => property.Id == id)).RowVersion);
    }
    private static FormUrlEncodedContent Form(string version) => new(new Dictionary<string, string> { ["ExpectedVersion"] = version });
    private sealed class FakeGeocoding : IGeocodingClient
    {
        public bool IsEnabled => true;
        public int Calls { get; private set; }
        public GeocodingResult Result { get; set; } = new(GeocodingStatus.Found, new(53.349805m, -6.26031m, "synthetic-place"));
        public Func<Task>? BeforeReturn { get; set; }
        public async Task<GeocodingResult> LookupAsync(string address, CancellationToken cancellationToken)
        { Calls++; if (BeforeReturn is not null) await BeforeReturn(); return Result; }
    }
    private sealed class FakeHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.geoapify.com", request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[{"lat":53.349805,"lon":-6.26031,"place_id":"synthetic-provider-place","result_type":"building","rank":{"confidence":0.95},"datasource":{"sourcename":"openstreetmap"}}]}""")
            });
        }
    }
}
