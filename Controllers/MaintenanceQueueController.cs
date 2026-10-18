using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Authorization;
using RentRight.Features.Maintenance;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize(Policy = RentalPolicies.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MaintenanceQueueController(ILandlordMaintenanceService service) : Controller
{
    public async Task<IActionResult> Index(MaintenanceQueueQuery query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(new MaintenanceQueueViewModel(query, new([], 0)));
        var result = await service.ListAsync(query, cancellationToken);
        switch (result.Status)
        {
            case TenantMaintenanceStatus.Success: return View(new MaintenanceQueueViewModel(query, result.Value!));
            case TenantMaintenanceStatus.Unauthenticated: return Challenge();
            case TenantMaintenanceStatus.Forbidden: return Forbid();
            case TenantMaintenanceStatus.Invalid:
                ModelState.AddModelError("", "Supply valid paging, status, property and date filters.");
                return View(new MaintenanceQueueViewModel(query, new([], 0)));
            default: return StatusCode(503);
        }
    }
}
