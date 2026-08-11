using Microsoft.AspNetCore.Mvc;
using RentRight.Controllers;
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;
using RentRight.Models;
using RentRight.ViewModels;
using Xunit;

namespace RentRight.Tests.Dashboards;

public class DashboardControllerTests
{
    [Theory]
    [InlineData(DashboardDestination.Admin, nameof(DashboardController.AdminDashboard))]
    [InlineData(DashboardDestination.Landlord, nameof(DashboardController.LandlordDashboard))]
    [InlineData(DashboardDestination.Tenant, nameof(DashboardController.TenantDashboard))]
    public void Index_RoleDestination_RedirectsToExpectedAction(
        DashboardDestination destination,
        string expectedAction)
    {
        var controller = CreateController(routeOutcome: DashboardRouteOutcome.Success(destination));

        var result = controller.Index();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(expectedAction, redirect.ActionName);
    }

    [Fact]
    public void Index_GenericDestination_ReturnsGenericView()
    {
        var controller = CreateController(
            routeOutcome: DashboardRouteOutcome.Success(DashboardDestination.Generic));

        var result = controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("GenericDashboard", view.ViewName);
    }

    [Fact]
    public void Index_UnauthenticatedOutcome_ReturnsChallenge()
    {
        var controller = CreateController(
            routeOutcome: DashboardRouteOutcome.Unauthenticated());

        var result = controller.Index();

        Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public async Task AdminDashboard_Success_MapsSummaryToViewModel()
    {
        var controller = CreateController(
            adminOutcome: AdminDashboardOutcome.Success(new AdminDashboardSummary(14, 6)));

        var result = await controller.AdminDashboard(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("AdminDashboard", view.ViewName);
        var model = Assert.IsType<AdminDashboardViewModel>(view.Model);
        Assert.Equal(14, model.TotalUsers);
        Assert.Equal(6, model.TotalProperties);
    }

    [Fact]
    public async Task LandlordDashboard_Success_MapsProjectionRatherThanEntity()
    {
        var summary = new LandlordDashboardSummary(
            [new LandlordPropertySummary(7, "Home", "Address", 1200m)]);
        var controller = CreateController(
            landlordOutcome: LandlordDashboardOutcome.Success(summary));

        var result = await controller.LandlordDashboard(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("LandlordDashboard", view.ViewName);
        var model = Assert.IsType<LandlordDashboardViewModel>(view.Model);
        var property = Assert.Single(model.Properties);
        Assert.Equal(7, property.Id);
        Assert.Equal("Home", property.Title);
    }

    [Fact]
    public async Task TenantDashboard_Success_MapsDashboardSpecificMaintenanceContract()
    {
        var summary = new TenantDashboardSummary(
            [new TenantTenancySummary(
                3,
                "Home",
                "Address",
                new DateOnly(2026, 8, 1),
                null,
                TenancyStatus.Active,
                true)],
            [new TenantMaintenanceSummary(
                9,
                "Home",
                new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
                MaintenanceRequestStatus.Submitted)]);
        var controller = CreateController(
            tenantOutcome: TenantDashboardOutcome.Success(summary));

        var result = await controller.TenantDashboard(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("TenantDashboard", view.ViewName);
        var model = Assert.IsType<TenantDashboardViewModel>(view.Model);
        Assert.Single(model.Tenancies);
        Assert.IsType<TenantDashboardMaintenanceRequestViewModel>(
            Assert.Single(model.RecentMaintenanceRequests));
    }

    [Theory]
    [InlineData(DashboardQueryStatus.Unauthenticated)]
    [InlineData(DashboardQueryStatus.Forbidden)]
    public async Task AdminDashboard_AuthorizationOutcome_MapsToExpectedMvcResult(
        DashboardQueryStatus status)
    {
        var outcome = status == DashboardQueryStatus.Unauthenticated
            ? AdminDashboardOutcome.Unauthenticated()
            : AdminDashboardOutcome.Forbidden();
        var controller = CreateController(adminOutcome: outcome);

        var result = await controller.AdminDashboard(CancellationToken.None);

        if (status == DashboardQueryStatus.Unauthenticated)
        {
            Assert.IsType<ChallengeResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    private static DashboardController CreateController(
        DashboardRouteOutcome? routeOutcome = null,
        AdminDashboardOutcome? adminOutcome = null,
        LandlordDashboardOutcome? landlordOutcome = null,
        TenantDashboardOutcome? tenantOutcome = null) =>
        new(
            new FakeRouteService(
                routeOutcome ?? DashboardRouteOutcome.Success(DashboardDestination.Generic)),
            new FakeAdminDashboardService(
                adminOutcome ?? AdminDashboardOutcome.Success(new AdminDashboardSummary(0, 0))),
            new FakeLandlordDashboardService(
                landlordOutcome ?? LandlordDashboardOutcome.Success(
                    new LandlordDashboardSummary([]))),
            new FakeTenantDashboardService(
                tenantOutcome ?? TenantDashboardOutcome.Success(
                    new TenantDashboardSummary([], []))));

    private sealed class FakeRouteService(DashboardRouteOutcome outcome)
        : IDashboardRouteService
    {
        public DashboardRouteOutcome Resolve() => outcome;
    }

    private sealed class FakeAdminDashboardService(AdminDashboardOutcome outcome)
        : IAdminDashboardQueryService
    {
        public Task<AdminDashboardOutcome> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }

    private sealed class FakeLandlordDashboardService(LandlordDashboardOutcome outcome)
        : ILandlordDashboardQueryService
    {
        public Task<LandlordDashboardOutcome> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }

    private sealed class FakeTenantDashboardService(TenantDashboardOutcome outcome)
        : ITenantDashboardQueryService
    {
        public Task<TenantDashboardOutcome> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }
}
