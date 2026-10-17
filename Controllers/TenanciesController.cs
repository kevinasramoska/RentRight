using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize(Roles = "Admin,Landlord,Tenant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TenanciesController(ITenancyService tenancyService) : Controller
{
    [Authorize(Roles = "Admin,Landlord")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var outcome = await tenancyService.ListForAdministrationAsync(cancellationToken);

        return outcome.Status switch
        {
            TenancyQueryStatus.Success => View(new TenancyAdminIndexViewModel
            {
                Tenancies = outcome.Tenancies!
                    .Select(ToViewModel)
                    .ToArray()
            }),
            TenancyQueryStatus.Unauthenticated => Challenge(),
            TenancyQueryStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Unknown tenancy list outcome.")
        };
    }

    [Authorize(Roles = "Admin,Landlord")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var outcome = await tenancyService.GetCreateFormAsync(cancellationToken);

        return outcome.Status switch
        {
            TenancyQueryStatus.Success => View(ToViewModel(outcome.Form!)),
            TenancyQueryStatus.Unauthenticated => Challenge(),
            TenancyQueryStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Unknown tenancy form outcome.")
        };
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Landlord")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        TenancyCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var outcome = await tenancyService.CreateAsync(
            new CreateTenancyCommand(
                model.PropertyId,
                model.TenantId,
                model.StartDate,
                model.EndDate),
            cancellationToken);

        switch (outcome.Status)
        {
            case CreateTenancyStatus.Success:
                return View("Invitation", outcome.Invitation);
            case CreateTenancyStatus.Unauthenticated:
                return Challenge();
            case CreateTenancyStatus.Forbidden:
                return Forbid();
            case CreateTenancyStatus.ValidationFailed:
                foreach (var error in outcome.Errors!)
                {
                    ModelState.AddModelError(error.Field, error.Message);
                }
                break;
            case CreateTenancyStatus.PropertyNotFound:
                ModelState.AddModelError(
                    nameof(model.PropertyId),
                    "Select a valid property.");
                break;
            case CreateTenancyStatus.Conflict:
                ModelState.AddModelError(
                    nameof(model.PropertyId),
                    "This property already has an active tenancy or pending invitation.");
                break;
            default:
                throw new InvalidOperationException("Unknown create-tenancy outcome.");
        }

        return await ReturnCreateViewAsync(model, cancellationToken);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Landlord")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> End(int id, CancellationToken cancellationToken, string? expectedVersion = null)
    {
        var outcome = await tenancyService.EndAsync(id, cancellationToken, expectedVersion);

        switch (outcome.Status)
        {
            case EndTenancyStatus.Success:
                if (outcome.Changed)
                {
                    TempData["Success"] = "Tenancy ended.";
                }
                return RedirectToAction(nameof(Index));
            case EndTenancyStatus.Unauthenticated:
                return Challenge();
            case EndTenancyStatus.Forbidden:
                return Forbid();
            case EndTenancyStatus.NotFound:
                return NotFound();
            case EndTenancyStatus.InvalidVersion:
                return BadRequest("Reload the tenancy and supply its version.");
            case EndTenancyStatus.Conflict:
                TempData["Error"] =
                    "The tenancy changed before it could be ended. Reload and try again.";
                return RedirectToAction(nameof(Index));
            default:
                throw new InvalidOperationException("Unknown end-tenancy outcome.");
        }
    }

    [Authorize(Roles = "Tenant")]
    public IActionResult Accept() => View(new TenancyAcceptViewModel());

    [HttpPost]
    [Authorize(Roles = "Tenant")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(TenancyAcceptViewModel model, CancellationToken cancellationToken)
    {
        var outcome = await tenancyService.AcceptAsync(new(model.Token), cancellationToken);
        if (outcome.Status == AcceptTenancyStatus.Success)
            return RedirectToAction("Index", "Dashboard");
        if (outcome.Status == AcceptTenancyStatus.Unauthenticated) return Challenge();
        if (outcome.Status == AcceptTenancyStatus.Forbidden) return Forbid();
        // Never echo a submitted token into a response or validation value.
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, "Invitation unavailable. Check the token, account and expiry with the sender.");
        return View(new TenancyAcceptViewModel());
    }

    private async Task<IActionResult> ReturnCreateViewAsync(
        TenancyCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var formOutcome = await tenancyService.GetCreateFormAsync(cancellationToken);
        switch (formOutcome.Status)
        {
            case TenancyQueryStatus.Success:
                ApplyOptions(model, formOutcome.Form!);
                return View(model);
            case TenancyQueryStatus.Unauthenticated:
                return Challenge();
            case TenancyQueryStatus.Forbidden:
                return Forbid();
            default:
                throw new InvalidOperationException("Unknown tenancy form outcome.");
        }
    }

    private static TenancyAdminRowViewModel ToViewModel(TenancyAdminRow tenancy) =>
        new()
        {
            Id = tenancy.Id,
            PropertyTitle = tenancy.PropertyTitle,
            PropertyAddress = tenancy.PropertyAddress,
            TenantName = tenancy.TenantName,
            TenantEmail = tenancy.TenantEmail,
            StartDate = tenancy.StartDate,
            EndDate = tenancy.EndDate,
            Version = tenancy.Version,
            Status = tenancy.Status
        };

    private static TenancyCreateViewModel ToViewModel(TenancyCreateForm form)
    {
        var model = new TenancyCreateViewModel { StartDate = form.DefaultStartDate };
        ApplyOptions(model, form);
        return model;
    }

    private static void ApplyOptions(
        TenancyCreateViewModel model,
        TenancyCreateForm form)
    {
        model.AvailableProperties = form.AvailableProperties
            .Select(option => new SelectListItem(option.Label, option.Value))
            .ToArray();
    }
}
