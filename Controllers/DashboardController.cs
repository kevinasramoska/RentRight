using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Features.Dashboards;
using RentRight.Features.Dashboards.Contracts;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize]
public class DashboardController(
    IDashboardRouteService routeService,
    IAdminDashboardQueryService adminDashboardService,
    ILandlordDashboardQueryService landlordDashboardService,
    ITenantDashboardQueryService tenantDashboardService) : Controller
{
    public IActionResult Index()
    {
        var outcome = routeService.Resolve();
        if (outcome.Status == DashboardQueryStatus.Unauthenticated)
        {
            return Challenge();
        }

        return outcome.Destination switch
        {
            DashboardDestination.Admin => RedirectToAction(nameof(AdminDashboard)),
            DashboardDestination.Landlord => RedirectToAction(nameof(LandlordDashboard)),
            DashboardDestination.Tenant => RedirectToAction(nameof(TenantDashboard)),
            DashboardDestination.Generic => View("GenericDashboard"),
            _ => throw new InvalidOperationException("Unknown dashboard destination.")
        };
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminDashboard(CancellationToken cancellationToken)
    {
        var outcome = await adminDashboardService.QueryAsync(cancellationToken);

        return outcome.Status switch
        {
            DashboardQueryStatus.Success => View(
                "AdminDashboard",
                new AdminDashboardViewModel
                {
                    TotalUsers = outcome.Dashboard!.TotalUsers,
                    TotalProperties = outcome.Dashboard.TotalProperties
                }),
            DashboardQueryStatus.Unauthenticated => Challenge(),
            DashboardQueryStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Unknown Admin dashboard outcome.")
        };
    }

    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> LandlordDashboard(CancellationToken cancellationToken)
    {
        var outcome = await landlordDashboardService.QueryAsync(cancellationToken);

        return outcome.Status switch
        {
            DashboardQueryStatus.Success => View(
                "LandlordDashboard",
                new LandlordDashboardViewModel
                {
                    Properties = outcome.Dashboard!.Properties
                        .Select(property => new LandlordDashboardPropertyViewModel
                        {
                            Id = property.Id,
                            Title = property.Title,
                            Address = property.Address,
                            RentAmount = property.RentAmount
                        })
                        .ToArray()
                }),
            DashboardQueryStatus.Unauthenticated => Challenge(),
            DashboardQueryStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Unknown Landlord dashboard outcome.")
        };
    }

    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> TenantDashboard(CancellationToken cancellationToken)
    {
        var outcome = await tenantDashboardService.QueryAsync(cancellationToken);

        return outcome.Status switch
        {
            DashboardQueryStatus.Success => View(
                "TenantDashboard",
                new TenantDashboardViewModel
                {
                    OpenRequestCount = outcome.Dashboard!.Counts.Open,
                    RecentRequestCount = outcome.Dashboard.Counts.Recent,
                    Tenancies = outcome.Dashboard.Tenancies
                        .Select(tenancy => new TenantTenancySummaryViewModel
                        {
                            Id = tenancy.Id,
                            PropertyTitle = tenancy.PropertyTitle,
                            PropertyAddress = tenancy.PropertyAddress,
                            StartDate = tenancy.StartDate,
                            EndDate = tenancy.EndDate,
                            Status = tenancy.Status,
                            IsCurrentlyActive = tenancy.IsCurrentlyActive
                        })
                        .ToArray(),
                    RecentMaintenanceRequests = outcome.Dashboard.RecentMaintenanceRequests
                        .Select(request => new TenantDashboardMaintenanceRequestViewModel
                        {
                            Id = request.Id,
                            PropertyTitle = request.PropertyTitle,
                            DateSubmitted = request.DateSubmitted,
                            Status = request.Status
                        })
                        .ToArray()
                }),
            DashboardQueryStatus.Unauthenticated => Challenge(),
            DashboardQueryStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Unknown Tenant dashboard outcome.")
        };
    }
}
