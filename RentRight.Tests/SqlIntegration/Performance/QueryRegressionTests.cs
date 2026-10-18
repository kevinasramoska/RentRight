using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using RentRight.Features.Dashboards;
using RentRight.Features.Maintenance;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.SqlIntegration.Performance;

[Trait("Category", "SqlIntegration")]
public sealed class QueryRegressionTests(SqlServerFixture server) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly QueryCapture _capture = new();
    private SqlApplication _app = null!;

    public async Task InitializeAsync()
    {
        _app = new(server.ConnectionString, _capture);
        await _app.InitializeAsync();
        await using var db = _app.CreateContext();
        await SyntheticQueryData.SeedAsync(db, 100);
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Fact]
    public async Task AdminUsers_PageSizesAndTiedEmails_UseFourCommandsAndCorrectBatchedRoles()
    {
        foreach (var size in new[] { 1, 50, 500 })
        {
            using var scope = _app.Services.CreateScope();
            var service = new AdministrationService(scope.ServiceProvider.GetRequiredService<IAdministrationRoleStore>(),
                new QueryUser("Admin", "Admin"));
            _capture.Commands.Clear();
            _capture.Enabled = true;
            var first = (await service.ListUsersAsync(new(1, size), default)).Page!;
            Assert.Equal(4, _capture.Commands.Count);
            _capture.Enabled = false;
            var second = (await service.ListUsersAsync(new(2, size), default)).Page!;
            Assert.Equal(Math.Min(size, 50), first.Users.Count);
            Assert.Empty(first.Users.Select(u => u.UserId).Intersect(second.Users.Select(u => u.UserId)));
            await using var db = _app.CreateContext();
            var expected = await db.Users.AsNoTracking().OrderBy(u => u.Email).ThenBy(u => u.Id)
                .Take(Math.Min(size, 50)).Select(u => u.Id).ToArrayAsync();
            Assert.Equal(expected, first.Users.Select(u => u.UserId));
            foreach (var row in first.Users)
            {
                var roles = await (from assignment in db.UserRoles join role in db.Roles on assignment.RoleId equals role.Id
                    where assignment.UserId == row.UserId orderby role.Name select role.Name!).ToArrayAsync();
                Assert.Equal(roles, row.Roles);
            }
        }
    }

    [Fact]
    public async Task PublicProperties_TiesRentBoundariesAndOversizePage_PreserveResultsWithoutTracking()
    {
        await using var db = _app.CreateContext();
        using var scope = _app.Services.CreateScope();
        var service = new PropertyService(new EfPropertyStore(db), new QueryUser("anonymous"),
            scope.ServiceProvider.GetRequiredService<IPropertyLocationService>());
        var large = (await service.SearchPublishedAsync(new(1, 500), default)).Value!;
        Assert.Equal(100, large.Items.Count);
        var first = (await service.SearchPublishedAsync(new(1, 7), default)).Value!;
        var second = (await service.SearchPublishedAsync(new(2, 7), default)).Value!;
        Assert.Equal(large.Items.Take(14).Select(p => p.Id), first.Items.Concat(second.Items).Select(p => p.Id));
        var filtered = (await service.SearchPublishedAsync(new(1, 100, "Home 1", 810, 819), default)).Value!;
        var expected = await db.Properties.AsNoTracking().Where(p => p.Title.Contains("Home 1") && p.RentAmount >= 810 && p.RentAmount <= 819)
            .OrderBy(p => p.Title).ThenBy(p => p.Address).ThenBy(p => p.Id).Select(p => p.Id).ToArrayAsync();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, filtered.Items.Select(p => p.Id));
        Assert.Equal(expected.Length, filtered.TotalCount);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task QueueAndDashboard_SyntheticSkew_PreserveOwnerAdminTenantScopeAndDateBoundaries()
    {
        await using var db = _app.CreateContext();
        var owner = new LandlordMaintenanceService(db, new QueryUser("OwningLandlord", "Landlord"));
        var other = new LandlordMaintenanceService(db, new QueryUser("OtherLandlord", "Landlord"));
        var admin = new LandlordMaintenanceService(db, new QueryUser("MultiRoleAdmin", "Admin", "Landlord", "Tenant"));
        var tenant = new LandlordMaintenanceService(db, new QueryUser("Tenant", "Tenant"));
        Assert.Equal(TenantMaintenanceStatus.Forbidden, (await tenant.ListAsync(new(), default)).Status);
        Assert.Empty((await other.ListAsync(new() { PropertyId = 1 }, default)).Value!.Items);
        var filter = new MaintenanceQueueQuery { PropertyId = 1, PageSize = 100,
            SubmittedFrom = new(2026, 10, 16), SubmittedTo = new(2026, 10, 16) };
        var page = (await owner.ListAsync(filter, default)).Value!;
        Assert.NotEmpty(page.Items);
        Assert.Equal(page.Items, (await admin.ListAsync(filter, default)).Value!.Items);
        Assert.All(page.Items, r => Assert.Equal(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), r.SubmittedUtc));
        var expected = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Tenancy!.PropertyId == 1 &&
            r.DateSubmitted >= new DateTime(2026, 10, 16) && r.DateSubmitted < new DateTime(2026, 10, 17))
            .OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id).Select(r => r.Id).ToArrayAsync();
        Assert.Equal(expected, page.Items.Select(r => r.Id));
        var first = (await owner.ListAsync(new() { PageSize = 100 }, default)).Value!;
        var next = (await owner.ListAsync(new() { Page = 2, PageSize = 100 }, default)).Value!;
        Assert.Equal(100, first.Items.Count);
        var ownerIds = await db.MaintenanceRequests.AsNoTracking()
            .Where(r => r.Tenancy!.Property!.LandlordId == "OwningLandlord")
            .OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id).Take(200).Select(r => r.Id).ToArrayAsync();
        Assert.Equal(ownerIds, first.Items.Concat(next.Items).Select(r => r.Id));
        Assert.Empty(first.Items.Select(r => r.Id).Intersect(next.Items.Select(r => r.Id)));
        Assert.Equal(TenantMaintenanceStatus.Invalid, (await owner.ListAsync(new() { PageSize = 101 }, default)).Status);
        var dashboard = (await new TenantDashboardQueryService(new EfDashboardDataStore(db),
            new QueryUser("Tenant", "Tenant"), new QueryClock()).QueryAsync(default)).Dashboard!;
        Assert.Single(dashboard.Tenancies);
        Assert.Equal(5, dashboard.RecentMaintenanceRequests.Count);
        var requests = await db.MaintenanceRequests.AsNoTracking().Where(r => r.Tenancy!.PropertyId == 1 && r.Tenancy.Status == Models.TenancyStatus.Active)
            .OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id).ToArrayAsync();
        Assert.Equal(requests.Take(5).Select(r => r.Id), dashboard.RecentMaintenanceRequests.Select(r => r.Id));
        Assert.Equal(requests.Count(r => r.Status is Models.MaintenanceRequestStatus.Submitted or Models.MaintenanceRequestStatus.InProgress), dashboard.Counts.Open);
        Assert.Equal(requests.Count(r => r.DateSubmitted >= new QueryClock().UtcNow.AddDays(-30)), dashboard.Counts.Recent);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
