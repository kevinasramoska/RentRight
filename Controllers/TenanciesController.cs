using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize(Roles = "Admin")]
public class TenanciesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public TenanciesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tenancies = await _context.Tenancies
            .AsNoTracking()
            .OrderByDescending(tenancy => tenancy.Status == TenancyStatus.Active)
            .ThenBy(tenancy => tenancy.Property!.Title)
            .Select(tenancy => new TenancyAdminRowViewModel
            {
                Id = tenancy.Id,
                PropertyTitle = tenancy.Property!.Title,
                PropertyAddress = tenancy.Property.Address,
                TenantName = tenancy.Tenant!.FullName,
                TenantEmail = tenancy.Tenant.Email ?? string.Empty,
                StartDate = tenancy.StartDate,
                EndDate = tenancy.EndDate,
                Status = tenancy.Status
            })
            .ToListAsync(cancellationToken);

        return View(new TenancyAdminIndexViewModel { Tenancies = tenancies });
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new TenancyCreateViewModel();
        await PopulateSelectionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        TenancyCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (model.StartDate == default)
        {
            ModelState.AddModelError(nameof(model.StartDate), "Enter a valid start date.");
        }

        if (model.EndDate < today)
        {
            ModelState.AddModelError(
                nameof(model.EndDate),
                "An active tenancy cannot have an end date in the past.");
        }

        Property? property = null;
        if (model.PropertyId.HasValue)
        {
            property = await _context.Properties
                .SingleOrDefaultAsync(item => item.Id == model.PropertyId.Value, cancellationToken);
        }

        if (property is null)
        {
            ModelState.AddModelError(nameof(model.PropertyId), "Select a valid property.");
        }

        var tenant = string.IsNullOrWhiteSpace(model.TenantId)
            ? null
            : await _userManager.FindByIdAsync(model.TenantId);

        if (tenant is null || !await _userManager.IsInRoleAsync(tenant, "Tenant"))
        {
            ModelState.AddModelError(nameof(model.TenantId), "Select a valid tenant account.");
        }

        if (model.PropertyId.HasValue && await _context.Tenancies.AnyAsync(
                tenancy => tenancy.PropertyId == model.PropertyId.Value &&
                    tenancy.Status == TenancyStatus.Active,
                cancellationToken))
        {
            ModelState.AddModelError(
                nameof(model.PropertyId),
                "This property already has an active tenancy.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateSelectionsAsync(model, cancellationToken);
            return View(model);
        }

        var tenancy = new Tenancy
        {
            PropertyId = property!.Id,
            TenantId = tenant!.Id,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = TenancyStatus.Active
        };

        _context.Tenancies.Add(tenancy);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(tenancy).State = EntityState.Detached;
            var activeTenancyNowExists = await _context.Tenancies
                .AsNoTracking()
                .AnyAsync(item =>
                        item.PropertyId == property.Id && item.Status == TenancyStatus.Active,
                    cancellationToken);

            if (!activeTenancyNowExists) throw;

            ModelState.AddModelError(
                nameof(model.PropertyId),
                "This property already has an active tenancy.");
            await PopulateSelectionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Tenancy created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> End(int id, CancellationToken cancellationToken)
    {
        var tenancy = await _context.Tenancies
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (tenancy is null) return NotFound();

        if (tenancy.Status == TenancyStatus.Active)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            tenancy.Status = TenancyStatus.Ended;
            tenancy.EndDate = tenancy.StartDate > today ? tenancy.StartDate : today;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Tenancy ended.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateSelectionsAsync(
        TenancyCreateViewModel model,
        CancellationToken cancellationToken)
    {
        model.AvailableProperties = await _context.Properties
            .AsNoTracking()
            .Where(property => !property.Tenancies.Any(tenancy =>
                tenancy.Status == TenancyStatus.Active))
            .OrderBy(property => property.Title)
            .Select(property => new SelectListItem
            {
                Value = property.Id.ToString(),
                Text = property.Title + " — " + property.Address
            })
            .ToListAsync(cancellationToken);

        var tenants = await _userManager.GetUsersInRoleAsync("Tenant");
        model.AvailableTenants = tenants
            .OrderBy(tenant => tenant.FullName)
            .ThenBy(tenant => tenant.Email)
            .Select(tenant => new SelectListItem
            {
                Value = tenant.Id,
                Text = string.IsNullOrWhiteSpace(tenant.FullName)
                    ? tenant.Email ?? tenant.UserName ?? tenant.Id
                    : tenant.FullName + " — " + tenant.Email
            })
            .ToList();
    }
}
