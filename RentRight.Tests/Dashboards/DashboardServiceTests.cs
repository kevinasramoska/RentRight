using RentRight.Application.Abstractions;
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;
using Xunit;

namespace RentRight.Tests.Dashboards;

public class DashboardServiceTests
{
    [Theory]
    [InlineData("Admin,Landlord,Tenant", DashboardDestination.Admin)]
    [InlineData("Landlord,Tenant", DashboardDestination.Landlord)]
    [InlineData("Tenant", DashboardDestination.Tenant)]
    [InlineData("", DashboardDestination.Generic)]
    public void Resolve_MultipleRoles_UsesDocumentedPrecedence(
        string roleList,
        DashboardDestination expectedDestination)
    {
        var roles = roleList.Length == 0 ? [] : roleList.Split(',');
        var service = new DashboardRouteService(
            new FakeCurrentUser(true, "user-1", roles));

        var outcome = service.Resolve();

        Assert.Equal(DashboardQueryStatus.Success, outcome.Status);
        Assert.Equal(expectedDestination, outcome.Destination);
    }

    [Fact]
    public void Resolve_UnauthenticatedUser_ReturnsUnauthenticated()
    {
        var service = new DashboardRouteService(
            new FakeCurrentUser(false, null, []));

        var outcome = service.Resolve();

        Assert.Equal(DashboardQueryStatus.Unauthenticated, outcome.Status);
    }

    [Fact]
    public async Task LandlordQuery_AuthenticatedLandlord_ScopesAndBoundsByCurrentUser()
    {
        var store = new FakeDashboardDataStore();
        var service = new LandlordDashboardQueryService(
            store,
            new FakeCurrentUser(true, "landlord-7", ["Landlord"]));

        var outcome = await service.QueryAsync(CancellationToken.None);

        Assert.Equal(DashboardQueryStatus.Success, outcome.Status);
        Assert.Equal("landlord-7", store.LandlordId);
        Assert.Equal(100, store.LandlordMaximumResults);
    }

    [Fact]
    public async Task TenantQuery_AuthenticatedTenant_ScopesBoundsAndUsesClockDate()
    {
        var store = new FakeDashboardDataStore();
        var service = new TenantDashboardQueryService(
            store,
            new FakeCurrentUser(true, "tenant-9", ["Tenant"]),
            new FakeClock(new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc)));

        var outcome = await service.QueryAsync(CancellationToken.None);

        Assert.Equal(DashboardQueryStatus.Success, outcome.Status);
        Assert.Equal("tenant-9", store.TenancyTenantId);
        Assert.Equal("tenant-9", store.MaintenanceTenantId);
        Assert.Equal(new DateOnly(2026, 8, 11), store.Today);
        Assert.Equal(100, store.TenancyMaximumResults);
        Assert.Equal(5, store.MaintenanceMaximumResults);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Landlord")]
    [InlineData("")]
    public async Task TenantQuery_UserWithoutTenantRole_ReturnsForbiddenWithoutQuerying(
        string role)
    {
        var store = new FakeDashboardDataStore();
        var roles = role.Length == 0 ? [] : new[] { role };
        var service = new TenantDashboardQueryService(
            store,
            new FakeCurrentUser(true, "user-1", roles),
            new FakeClock(DateTime.UnixEpoch));

        var outcome = await service.QueryAsync(CancellationToken.None);

        Assert.Equal(DashboardQueryStatus.Forbidden, outcome.Status);
        Assert.Null(store.TenancyTenantId);
        Assert.Null(store.MaintenanceTenantId);
    }

    [Fact]
    public async Task AdminQuery_AdminUser_ReturnsProjectedCounts()
    {
        var store = new FakeDashboardDataStore
        {
            AdminSummary = new AdminDashboardSummary(12, 4)
        };
        var service = new AdminDashboardQueryService(
            store,
            new FakeCurrentUser(true, "admin-1", ["Admin"]));

        var outcome = await service.QueryAsync(CancellationToken.None);

        Assert.Equal(DashboardQueryStatus.Success, outcome.Status);
        Assert.Equal(12, outcome.Dashboard!.TotalUsers);
        Assert.Equal(4, outcome.Dashboard.TotalProperties);
    }

    private sealed class FakeDashboardDataStore : IDashboardDataStore
    {
        public AdminDashboardSummary AdminSummary { get; init; } = new(0, 0);
        public string? LandlordId { get; private set; }
        public int LandlordMaximumResults { get; private set; }
        public string? TenancyTenantId { get; private set; }
        public int TenancyMaximumResults { get; private set; }
        public DateOnly? Today { get; private set; }
        public string? MaintenanceTenantId { get; private set; }
        public int MaintenanceMaximumResults { get; private set; }

        public Task<AdminDashboardSummary> GetAdminSummaryAsync(
            CancellationToken cancellationToken) => Task.FromResult(AdminSummary);

        public Task<IReadOnlyList<LandlordPropertySummary>> ListLandlordPropertiesAsync(
            string landlordId,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            LandlordId = landlordId;
            LandlordMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<LandlordPropertySummary>>([]);
        }

        public Task<IReadOnlyList<TenantTenancySummary>> ListTenantTenanciesAsync(
            string tenantId,
            DateOnly today,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            TenancyTenantId = tenantId;
            Today = today;
            TenancyMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<TenantTenancySummary>>([]);
        }

        public Task<IReadOnlyList<TenantMaintenanceSummary>> ListTenantMaintenanceAsync(
            string tenantId,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            MaintenanceTenantId = tenantId;
            MaintenanceMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<TenantMaintenanceSummary>>([]);
        }
    }

    private sealed class FakeCurrentUser(
        bool isAuthenticated,
        string? userId,
        IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public string? UserId => userId;
        public bool IsInRole(string roleName) => roles.Contains(roleName);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
