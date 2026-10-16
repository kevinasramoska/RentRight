using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RentRight.Api;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.Api;

public class PropertiesApiTests
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string ProbeRoute = "/api/v1/api-convention-probe";

    [Theory]
    [InlineData("Landlord")]
    [InlineData("Admin")]
    public async Task CreateForm_Manager_SubmitsToMvcAndCreatesProperty(string role)
    {
        var store = new InMemoryPropertyStore();
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "manager-1", role);

        using var page = await client.GetAsync("/Properties/Create");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var form = Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.Singleline)
            .Cast<Match>()
            .Single(match => match.Value.Contains("name=\"Title\"", StringComparison.Ordinal))
            .Value;
        var action = WebUtility.HtmlDecode(Regex.Match(form, "action=\"([^\"]+)\"").Groups[1].Value);
        var token = WebUtility.HtmlDecode(Regex.Match(
            form,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

        Assert.Equal("/Properties/Create", action);
        Assert.NotEmpty(token);
        using var response = await client.PostAsync(action, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Title"] = "MVC Apartment",
                ["Address"] = "1 Main Street",
                ["RentAmount"] = "1250",
                ["Description"] = "Created through the actual Razor form",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Properties", response.Headers.Location?.OriginalString);
        var property = Assert.Single(store.Properties);
        Assert.Equal("MVC Apartment", property.Title);
        Assert.Equal("manager-1", property.LandlordId);
    }

    [Fact]
    public async Task GetProperties_Anonymous_FiltersPagesAndOrdersPublishedProjection()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Zeta Apartment", 1_400m),
            Property(2, "Alpha Apartment", 1_200m),
            Property(3, "Hidden Apartment", 1_300m, published: false),
            Property(4, "Budget House", 900m));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            PropertiesRoute +
            "?status=published&search=apartment&minimumRent=1000&maximumRent=1500" +
            "&page=1&pageSize=1");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("totalPages").GetInt32());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("id").GetInt32());
        Assert.Equal("Alpha Apartment", item.GetProperty("title").GetString());
        Assert.False(item.TryGetProperty("landlordId", out _));
        Assert.False(item.TryGetProperty("canManage", out _));
        Assert.DoesNotContain("Hidden Apartment", body);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("status=archived", "status")]
    [InlineData("minimumRent=2000&maximumRent=1000", "minimumRent")]
    public async Task GetProperties_InvalidFilter_ReturnsValidationProblem(
        string query,
        string expectedField)
    {
        await using var factory = CreateFactory(new InMemoryPropertyStore());
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(PropertiesRoute + '?' + query);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement
            .GetProperty("errors")
            .TryGetProperty(expectedField, out _));
    }

    [Fact]
    public async Task GetProperty_Published_ReturnsPublicJsonSchema()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Apartment", 1_200m));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(PropertiesRoute + "/1");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Apartment", document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("landlordId", out _));
        Assert.False(document.RootElement.TryGetProperty("tenancies", out _));
        Assert.False(document.RootElement.TryGetProperty("maintenanceRequests", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetProperty_Unpublished_IsConcealedFromAnonymousAndTenant(
        bool tenant)
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Hidden", 1_200m, published: false));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        if (tenant)
        {
            AddIdentityCookie(client, factory, "tenant-1", "Tenant");
        }

        using var response = await client.GetAsync(PropertiesRoute + "/1");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ResourceNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthenticationRequired)]
    [InlineData("Tenant", HttpStatusCode.Forbidden, ApiErrorCodes.AccessDenied)]
    public async Task PostProperty_NonManager_ReturnsAuthenticationOrAuthorizationProblem(
        string? role,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await using var factory = CreateFactory(new InMemoryPropertyStore());
        using var client = CreateClient(factory);
        if (role is not null)
        {
            AddIdentityCookie(client, factory, "tenant-1", role);
        }

        using var response = await client.PostAsJsonAsync(
            PropertiesRoute,
            ValidPropertyRequest());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostProperty_Landlord_ReturnsCreatedLocationAndIgnoresForgedFields()
    {
        var store = new InMemoryPropertyStore();
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);
        using var request = new StringContent(
            """
            {
              "title": "Created Apartment",
              "address": "1 Main Street",
              "rentAmount": 1250,
              "description": "Public description",
              "landlordId": "forged-owner",
              "status": "archived",
              "id": 999,
              "tenancies": [{ "id": 8 }]
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(PropertiesRoute, request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/v1/properties/1", response.Headers.Location?.AbsolutePath);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, document.RootElement.GetProperty("id").GetInt32());
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
        var created = Assert.Single(store.Properties);
        Assert.Equal("landlord-1", created.LandlordId);
        Assert.Equal("Created Apartment", created.Title);
    }

    [Fact]
    public async Task PostProperty_InvalidBody_ReturnsFieldValidationWithoutPersistence()
    {
        var store = new InMemoryPropertyStore();
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PostAsJsonAsync(
            PropertiesRoute,
            new { title = "", address = "", rentAmount = -1m });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("address", out _));
        Assert.True(errors.TryGetProperty("rentAmount", out _));
        Assert.Empty(store.Properties);
    }

    [Fact]
    public async Task PutProperty_OwningLandlord_UpdatesPermittedFieldsOnly()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Before", 1_100m, landlordId: "landlord-1"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PutAsJsonAsync(
            PropertiesRoute + "/1",
            new
            {
                title = "After",
                address = "Updated address",
                rentAmount = 1_500m,
                description = "Updated",
                landlordId = "forged-owner",
                id = 2
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        var updated = Assert.Single(store.Properties);
        Assert.Equal("After", updated.Title);
        Assert.Equal("landlord-1", updated.LandlordId);
    }

    [Fact]
    public async Task PutProperty_InvalidBody_ReturnsFieldValidationWithoutUpdate()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Before", 1_100m, landlordId: "landlord-1"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PutAsJsonAsync(
            PropertiesRoute + "/1",
            new { title = "", address = "Address", rentAmount = -1m });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("Before", Assert.Single(store.Properties).Title);
    }

    [Fact]
    public async Task PutProperty_OtherLandlord_ConcealsResource()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Protected", 1_100m, landlordId: "landlord-2"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PutAsJsonAsync(
            PropertiesRoute + "/1",
            ValidPropertyRequest());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ResourceNotFound,
            problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("Protected", Assert.Single(store.Properties).Title);
    }

    [Fact]
    public async Task PutProperty_AdminAndLandlordRoles_ExplicitAdminOverrideUpdatesNonOwner()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Before", 1_100m, landlordId: "landlord-2"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "admin-1", "Admin", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PutAsJsonAsync(
            PropertiesRoute + "/1",
            ValidPropertyRequest("Admin update"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = Assert.Single(store.Properties);
        Assert.Equal("Admin update", updated.Title);
        Assert.Equal("landlord-2", updated.LandlordId);
    }

    [Fact]
    public async Task DeleteProperty_OwnerWithoutHistory_DeletesAndReturnsNoContent()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Unused", 1_100m, landlordId: "landlord-1"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.DeleteAsync(PropertiesRoute + "/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(store.Properties);
    }

    [Fact]
    public async Task DeleteProperty_OwnerWithHistory_ReturnsConflictAndRetainsProperty()
    {
        var store = new InMemoryPropertyStore(
            Property(
                1,
                "Historical",
                1_100m,
                landlordId: "landlord-1",
                hasHistory: true));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.DeleteAsync(PropertiesRoute + "/1");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ResourceConflict,
            problem.RootElement.GetProperty("code").GetString());
        Assert.Single(store.Properties);
    }

    [Fact]
    public async Task DeleteProperty_OtherLandlord_ConcealsResource()
    {
        var store = new InMemoryPropertyStore(
            Property(1, "Protected", 1_100m, landlordId: "landlord-2"));
        await using var factory = CreateFactory(store);
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.DeleteAsync(PropertiesRoute + "/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(store.Properties);
    }

    [Fact]
    public async Task PutProperty_MissingResource_ReturnsNotFoundProblem()
    {
        await using var factory = CreateFactory(new InMemoryPropertyStore());
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");
        await AddAntiforgeryTokenAsync(client);

        using var response = await client.PutAsJsonAsync(
            PropertiesRoute + "/404",
            ValidPropertyRequest());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ResourceNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostProperty_AuthenticatedWithoutAntiforgeryHeader_IsRejected()
    {
        await using var factory = CreateFactory(new InMemoryPropertyStore());
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory, "landlord-1", "Landlord");

        using var response = await client.PostAsJsonAsync(
            PropertiesRoute,
            ValidPropertyRequest());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.InvalidRequest,
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOpenApi_Properties_DescribesRoutesSchemasSecurityAndResponses()
    {
        await using var factory = CreateFactory(new InMemoryPropertyStore());
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ApiConventions.OpenApiDocumentPath);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty(PropertiesRoute);
        Assert.True(collection.TryGetProperty("get", out var get));
        Assert.True(collection.TryGetProperty("post", out var post));
        var item = paths.GetProperty(PropertiesRoute + "/{propertyId}");
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out var put));
        Assert.True(item.TryGetProperty("delete", out var delete));
        Assert.False(get.TryGetProperty("security", out _));
        Assert.True(post.TryGetProperty("security", out _));
        Assert.True(put.TryGetProperty("security", out _));
        Assert.True(delete.TryGetProperty("security", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(put.GetProperty("responses").TryGetProperty("204", out _));
        Assert.True(delete.GetProperty("responses").TryGetProperty("409", out _));
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenancies", body, StringComparison.OrdinalIgnoreCase);
    }

    private static RentRightApplicationFactory CreateFactory(
        InMemoryPropertyStore store) =>
        new("Development", services =>
        {
            services.RemoveAll<IPropertyStore>();
            services.AddSingleton<IPropertyStore>(store);
        });

    private static HttpClient CreateClient(RentRightApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static void AddIdentityCookie(
        HttpClient client,
        RentRightApplicationFactory factory,
        string userId,
        params string[] roles)
    {
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(
            claims,
            IdentityConstants.ApplicationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            IdentityConstants.ApplicationScheme);
        var cookieValue = cookieOptions.TicketDataFormat.Protect(ticket);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={cookieValue}");
    }

    private static async Task AddAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ProbeRoute + "/antiforgery");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(
            ApiConventions.AntiforgeryHeaderName,
            document.RootElement.GetProperty("requestToken").GetString());
    }

    private static object ValidPropertyRequest(string title = "Apartment") =>
        new
        {
            title,
            address = "1 Main Street",
            rentAmount = 1_250m,
            description = "Description"
        };

    private static StoredProperty Property(
        int id,
        string title,
        decimal rentAmount,
        string landlordId = "landlord-1",
        bool published = true,
        bool hasHistory = false) =>
        new(
            id,
            title,
            $"{id} Main Street",
            rentAmount,
            "Description",
            landlordId,
            published,
            hasHistory);

    private sealed record StoredProperty(
        int Id,
        string Title,
        string Address,
        decimal RentAmount,
        string? Description,
        string LandlordId,
        bool Published,
        bool HasHistory);

    private sealed class InMemoryPropertyStore : IPropertyStore
    {
        private readonly Dictionary<int, StoredProperty> _properties;

        public InMemoryPropertyStore(params StoredProperty[] properties)
        {
            _properties = properties.ToDictionary(property => property.Id);
        }

        public IReadOnlyCollection<StoredProperty> Properties =>
            _properties.Values.ToArray();

        public Task<PublishedPropertyPageRecord> SearchPublishedAsync(
            PublishedPropertyQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var properties = _properties.Values.Where(property => property.Published);
            if (query.Search is not null)
            {
                properties = properties.Where(property =>
                    property.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                    property.Address.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            }

            if (query.MinimumRent is decimal minimumRent)
            {
                properties = properties.Where(property => property.RentAmount >= minimumRent);
            }

            if (query.MaximumRent is decimal maximumRent)
            {
                properties = properties.Where(property => property.RentAmount <= maximumRent);
            }

            var filtered = properties.ToArray();
            IReadOnlyList<PublishedPropertyListItem> items = filtered
                .OrderBy(property => property.Title)
                .ThenBy(property => property.Address)
                .ThenBy(property => property.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(ToPublishedItem)
                .ToArray();
            return Task.FromResult(new PublishedPropertyPageRecord(items, filtered.Length));
        }

        public Task<PublishedPropertyListItem?> FindPublishedAsync(
            int propertyId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var property = _properties.GetValueOrDefault(propertyId);
            return Task.FromResult(
                property is { Published: true } ? ToPublishedItem(property) : null);
        }

        public Task<IReadOnlyList<PropertyListRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<PropertyListRecord> items = _properties.Values
                .OrderBy(property => property.Title)
                .Take(limit)
                .Select(property => new PropertyListRecord(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.LandlordId))
                .ToArray();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<LatestPropertyListItem>> ListLatestAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<LatestPropertyListItem> items = _properties.Values
                .OrderByDescending(property => property.Id)
                .Take(limit)
                .Select(property => new LatestPropertyListItem(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.Description))
                .ToArray();
            return Task.FromResult(items);
        }

        public Task<PropertyRecord?> FindAsync(
            int propertyId,
            CancellationToken cancellationToken)
        {
            var property = _properties.GetValueOrDefault(propertyId);
            return Task.FromResult(property is null
                ? null
                : new PropertyRecord(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.Description,
                    property.LandlordId));
        }

        public Task<int> CreateAsync(
            PropertyValues values,
            string landlordId,
            CancellationToken cancellationToken)
        {
            var id = _properties.Count == 0 ? 1 : _properties.Keys.Max() + 1;
            _properties[id] = new StoredProperty(
                id,
                values.Title,
                values.Address,
                values.RentAmount,
                values.Description,
                landlordId,
                Published: true,
                HasHistory: false);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            int propertyId,
            PropertyValues values,
            CancellationToken cancellationToken)
        {
            if (!_properties.TryGetValue(propertyId, out var property))
            {
                return Task.FromResult(false);
            }

            _properties[propertyId] = property with
            {
                Title = values.Title,
                Address = values.Address,
                RentAmount = values.RentAmount,
                Description = values.Description
            };
            return Task.FromResult(true);
        }

        public Task<PropertyDeleteStoreStatus> DeleteAsync(
            int propertyId,
            CancellationToken cancellationToken)
        {
            if (!_properties.TryGetValue(propertyId, out var property))
            {
                return Task.FromResult(PropertyDeleteStoreStatus.NotFound);
            }

            if (property.HasHistory)
            {
                return Task.FromResult(PropertyDeleteStoreStatus.HasHistory);
            }

            _properties.Remove(propertyId);
            return Task.FromResult(PropertyDeleteStoreStatus.Deleted);
        }

        private static PublishedPropertyListItem ToPublishedItem(StoredProperty property) =>
            new(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.Description);
    }
}
