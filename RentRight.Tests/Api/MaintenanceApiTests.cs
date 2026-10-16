using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RentRight.Api;
using RentRight.Features.Maintenance;
using Xunit;

namespace RentRight.Tests.Api;

public class MaintenanceApiTests
{
    private const string Route = "/api/v1/maintenance-requests";

    [Theory]
    [InlineData(TenantMaintenanceStatus.Success, 201)]
    [InlineData(TenantMaintenanceStatus.NoActiveTenancy, 409)]
    [InlineData(TenantMaintenanceStatus.Conflict, 409)]
    [InlineData(TenantMaintenanceStatus.Invalid, 400)]
    [InlineData(TenantMaintenanceStatus.Unavailable, 503)]
    public async Task Create_ServiceOutcome_MapsSafeContract(TenantMaintenanceStatus status, int expected)
    {
        var service = new Service { Status = status };
        await using var factory = Factory(service);
        using var client = Client(factory, "Tenant");
        await AntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(Route, new { description = "A leaking kitchen sink", tenancyId = 999,
            tenantId = "forged", status = "Closed", submittedUtc = "2000-01-01T00:00:00Z" });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("A leaking kitchen sink", service.Description);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("forged", body);
        if (expected == 201)
        {
            Assert.EndsWith(Route + "/7", response.Headers.Location!.ToString());
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        else await ProblemAsync(response, expected);
    }

    [Fact]
    public async Task Create_CookieWithoutAntiforgery_RejectsBeforeService()
    {
        var service = new Service();
        await using var factory = Factory(service);
        using var client = Client(factory, "Tenant");
        using var response = await client.PostAsJsonAsync(Route, new { description = "A leaking kitchen sink" });
        await ProblemAsync(response, 400);
        Assert.Null(service.Description);
    }

    [Theory]
    [InlineData("", 401)]
    [InlineData("Landlord", 403)]
    [InlineData("Admin", 403)]
    public async Task TenantEndpoints_AnonymousOrWrongRole_Rejects(string role, int expected)
    {
        await using var factory = Factory(new Service());
        using var client = Client(factory, role.Length == 0 ? [] : [role]);
        using var response = await client.GetAsync(Route);
        await ProblemAsync(response, expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task Create_InvalidDescription_ReturnsValidation(string? description)
    {
        var service = new Service();
        await using var factory = Factory(service);
        using var client = Client(factory, "Tenant");
        await AntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(Route, new { description });
        await ProblemAsync(response, 400);
        Assert.Null(service.Description);
    }

    [Fact]
    public async Task Read_PagingAndDetail_UseBoundedSafeContracts()
    {
        await using var factory = Factory(new Service());
        using var client = Client(factory, "Tenant");
        using var list = await client.GetAsync(Route + "?pageSize=1");
        list.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        using var invalid = await client.GetAsync(Route + "?pageSize=101");
        await ProblemAsync(invalid, 400);
        using var missing = await client.GetAsync(Route + "/99");
        await ProblemAsync(missing, 404);
        using var detail = await client.GetAsync(Route + "/7");
        detail.EnsureSuccessStatusCode();
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Submitted", body);
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_Maintenance_DescribesContractsAndCookieSecurity()
    {
        await using var factory = Factory(new Service());
        using var client = Client(factory);
        using var response = await client.GetAsync(ApiConventions.OpenApiDocumentPath);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var post = doc.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("post");
        Assert.True(post.TryGetProperty("security", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("503", out _));
        var input = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("MaintenanceSubmissionRequest");
        Assert.Single(input.GetProperty("properties").EnumerateObject());
    }

    [Theory]
    [InlineData(TenantMaintenanceStatus.Unauthenticated, 401)]
    [InlineData(TenantMaintenanceStatus.Forbidden, 403)]
    [InlineData(TenantMaintenanceStatus.NotFound, 404)]
    [InlineData(TenantMaintenanceStatus.Invalid, 400)]
    [InlineData(TenantMaintenanceStatus.Unavailable, 503)]
    public async Task LandlordDetail_ServiceFailure_ReturnsStandardProblem(TenantMaintenanceStatus status, int expected)
    {
        await using var factory = new RentRightApplicationFactory("Development", services =>
        {
            services.RemoveAll<ILandlordMaintenanceService>();
            services.AddSingleton<ILandlordMaintenanceService>(new LandlordQueries(status));
        });
        using var client = Client(factory, "Admin", "Tenant", "Landlord");
        using var response = await client.GetAsync("/api/v1/landlord-maintenance-requests/7");
        await ProblemAsync(response, expected);
    }

    private sealed class LandlordQueries(TenantMaintenanceStatus status) : ILandlordMaintenanceService
    {
        public Task<TenantMaintenanceResult<MaintenanceQueuePage>> ListAsync(MaintenanceQueueQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<MaintenanceQueuePage>(status));
        public Task<TenantMaintenanceResult<MaintenanceTriageDetail>> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<MaintenanceTriageDetail>(status));
    }

    private static async Task ProblemAsync(HttpResponseMessage response, int expected)
    {
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("code", out _));
        Assert.True(doc.RootElement.TryGetProperty("traceId", out _));
    }
    private static RentRightApplicationFactory Factory(Service service) => new("Development", services =>
    {
        services.RemoveAll<ITenantMaintenanceService>();
        services.AddSingleton<ITenantMaintenanceService>(service);
    });
    private static HttpClient Client(RentRightApplicationFactory factory, params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        if (roles.Length == 0) return client;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "actor") }.Concat(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }
    private static async Task AntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/api-convention-probe/antiforgery");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName, doc.RootElement.GetProperty("requestToken").GetString());
    }
    private sealed class Service : ITenantMaintenanceService
    {
        public TenantMaintenanceStatus Status = TenantMaintenanceStatus.Success;
        public string? Description;
        private static readonly TenantMaintenanceItem Item = new(7, "Home", "Address", "A leaking kitchen sink",
            DateTime.UnixEpoch, DateTime.UnixEpoch, "Submitted");
        public Task<TenantMaintenanceResult<int>> CreateAsync(string? description, CancellationToken cancellationToken)
        {
            Description = description;
            return Task.FromResult(new TenantMaintenanceResult<int>(Status, 7));
        }
        public Task<TenantMaintenanceResult<TenantMaintenancePage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<TenantMaintenancePage>(Status, new([Item], 1)));
        public Task<TenantMaintenanceResult<TenantMaintenanceDetail>> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<TenantMaintenanceDetail>(id == 7 ? Status : TenantMaintenanceStatus.NotFound,
                new(Item, [new("Submitted", DateTime.UnixEpoch, false)])));
    }
}
