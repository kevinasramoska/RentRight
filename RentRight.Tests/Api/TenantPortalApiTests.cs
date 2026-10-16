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
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;
using RentRight.Features.Maintenance;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Api;

public class TenantPortalApiTests
{
    private const string Dashboard = "/api/v1/tenant-dashboard";

    [Theory]
    [InlineData("", 401)]
    [InlineData("Landlord", 403)]
    [InlineData("Admin", 403)]
    public async Task Dashboard_AnonymousOrWrongRole_ReturnsStandardProblemWithoutQuery(string role, int expected)
    {
        var query = new Query();
        await using var factory = Factory(query);
        using var client = Client(factory, role);
        using var response = await client.GetAsync(Dashboard);
        await AssertProblem(response, expected);
        Assert.Equal(0, query.Calls);
    }

    [Theory]
    [InlineData(DashboardQueryStatus.Unauthenticated, 401)]
    [InlineData(DashboardQueryStatus.Forbidden, 403)]
    public async Task Dashboard_ServiceAccessLoss_ReturnsStandardProblem(DashboardQueryStatus status, int expected)
    {
        await using var factory = Factory(new Query { Status = status });
        using var client = Client(factory, "Tenant");
        using var response = await client.GetAsync(Dashboard);
        await AssertProblem(response, expected);
    }

    [Fact]
    public async Task Dashboard_Unavailable_ReturnsSafeRetryableProblem()
    {
        await using var factory = Factory(new Query { Unavailable = true });
        using var client = Client(factory, "Tenant");
        using var response = await client.GetAsync(Dashboard);
        await AssertProblem(response, 503);
        Assert.DoesNotContain("private exception", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_Projection_OmitsInternalIdentifiersAndUsesUtc(bool empty)
    {
        await using var factory = Factory(new Query { Empty = empty });
        using var client = Client(factory, "Tenant");
        using var response = await client.GetAsync(Dashboard);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(empty ? 0 : 1, json.RootElement.GetProperty("tenancies").GetArrayLength());
        Assert.DoesNotContain("tenancyId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("landlordId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actorUserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("propertyId", body, StringComparison.OrdinalIgnoreCase);
        if (!empty)
        {
            var home = json.RootElement.GetProperty("tenancies")[0];
            Assert.Equal(["propertyTitle", "propertyAddress", "startDate"], home.EnumerateObject().Select(p => p.Name));
            Assert.EndsWith("Z", json.RootElement.GetProperty("recentMaintenanceRequests")[0].GetProperty("submittedUtc").GetString());
        }
    }

    [Fact]
    public async Task PortalSmoke_CookieHostSessionDashboardCsrfSubmitDetailAndList_WorkTogether()
    {
        // Real host/middleware/cookie/CSRF/HTTP contracts; SQL journey separately covers persistence.
        await using var factory = Factory(new Query());
        using var client = Client(factory, "Tenant");
        using var host = await client.GetAsync("/app/requests/new");
        Assert.Contains("id=\"root\"", await host.Content.ReadAsStringAsync());
        using var session = await client.GetAsync("/api/v1/session");
        Assert.Contains("Tenant", await session.Content.ReadAsStringAsync());
        using var dashboard = await client.GetAsync(Dashboard);
        dashboard.EnsureSuccessStatusCode();
        using var csrf = await client.GetAsync("/api/v1/session/antiforgery");
        using var token = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/v1/maintenance-requests", new { description = "Kitchen tap leaking" });
        Assert.Equal(201, (int)created.StatusCode);
        using var detail = await client.GetAsync(created.Headers.Location);
        detail.EnsureSuccessStatusCode();
        Assert.Contains("Kitchen tap leaking", await detail.Content.ReadAsStringAsync());
        using var list = await client.GetAsync("/api/v1/maintenance-requests?page=1&pageSize=10");
        list.EnsureSuccessStatusCode();
        Assert.Contains("Kitchen tap leaking", await list.Content.ReadAsStringAsync());
    }

    private static async Task AssertProblem(HttpResponseMessage response, int expected)
    {
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("code", out _));
    }

    private static RentRightApplicationFactory Factory(Query query) => new("Production", services =>
    {
        services.RemoveAll<ITenantDashboardQueryService>();
        services.AddSingleton<ITenantDashboardQueryService>(query);
        services.RemoveAll<ITenantMaintenanceService>();
        services.AddSingleton<ITenantMaintenanceService, Maintenance>();
    });

    private static HttpClient Client(RentRightApplicationFactory factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role.Length == 0) return client;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "private-internal-id"), new Claim(ClaimTypes.Role, role)], IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    private sealed class Query : ITenantDashboardQueryService
    {
        public DashboardQueryStatus Status { get; init; } = DashboardQueryStatus.Success;
        public bool Empty { get; init; }
        public bool Unavailable { get; init; }
        public int Calls { get; private set; }
        public Task<TenantDashboardOutcome> QueryAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Unavailable) throw new TimeoutException("private exception");
            return Task.FromResult(new TenantDashboardOutcome(Status, new TenantDashboardSummary(
                Empty ? [] : [new(98123, "Home", "Address", new(2026, 1, 1), null, TenancyStatus.Active, true)],
                Empty ? [] : [new(7, "Home", DateTime.UnixEpoch, MaintenanceRequestStatus.Submitted)]) { Counts = new(Empty ? 0 : 1, Empty ? 0 : 1) }));
        }
    }

    private sealed class Maintenance : ITenantMaintenanceService
    {
        private TenantMaintenanceItem? _item;
        public Task<TenantMaintenanceResult<int>> CreateAsync(string? description, CancellationToken cancellationToken)
        {
            _item = new(7, "Home", "Address", description!, DateTime.UnixEpoch, DateTime.UnixEpoch, "Submitted");
            return Task.FromResult(new TenantMaintenanceResult<int>(TenantMaintenanceStatus.Success, 7));
        }
        public Task<TenantMaintenanceResult<TenantMaintenancePage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<TenantMaintenancePage>(TenantMaintenanceStatus.Success, new(_item is null ? [] : [_item], _item is null ? 0 : 1)));
        public Task<TenantMaintenanceResult<TenantMaintenanceDetail>> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantMaintenanceResult<TenantMaintenanceDetail>(TenantMaintenanceStatus.Success, new(_item!, [new("Submitted", DateTime.UnixEpoch, false)])));
    }
}
