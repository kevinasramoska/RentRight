using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize(Roles = "Tenant,Landlord,Admin")]
public class MaintenanceRequestController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public MaintenanceRequestController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var requests = await AuthorizedRequests(userId)
            .AsNoTracking()
            .OrderByDescending(request => request.DateSubmitted)
            .Select(request => new MaintenanceRequestRowViewModel
            {
                Id = request.Id,
                PropertyTitle = request.Tenancy!.Property!.Title,
                TenantName = request.Tenancy.Tenant!.FullName,
                Description = request.Description,
                DateSubmitted = request.DateSubmitted,
                Status = request.Status,
                CanUpdateStatus = User.IsInRole("Admin") ||
                    (User.IsInRole("Landlord") && request.Tenancy.Property.LandlordId == userId)
            })
            .ToListAsync(cancellationToken);

        var heading = User.IsInRole("Admin")
            ? "All Maintenance Requests"
            : User.IsInRole("Landlord")
                ? "Maintenance Requests for My Properties"
                : "My Maintenance Requests";

        return View(new MaintenanceRequestIndexViewModel
        {
            Heading = heading,
            CanCreate = User.IsInRole("Tenant"),
            Requests = requests
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var request = await AuthorizedRequests(userId)
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MaintenanceRequestDetailsViewModel
            {
                Id = item.Id,
                PropertyTitle = item.Tenancy!.Property!.Title,
                PropertyAddress = item.Tenancy.Property.Address,
                TenantName = item.Tenancy.Tenant!.FullName,
                Description = item.Description,
                DateSubmitted = item.DateSubmitted,
                LastUpdatedUtc = item.LastUpdatedUtc,
                Status = item.Status,
                CanUpdateStatus = User.IsInRole("Admin") ||
                    (User.IsInRole("Landlord") && item.Tenancy.Property.LandlordId == userId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return request is null ? NotFound() : View(request);
    }

    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var model = new MaintenanceRequestCreateViewModel();
        await PopulateAvailableTenanciesAsync(model, userId, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> Create(
        MaintenanceRequestCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var description = model.Description.Trim();
        if (description.Length < 10)
        {
            ModelState.AddModelError(
                nameof(model.Description),
                "Description must contain at least 10 non-whitespace characters.");
        }

        Tenancy? tenancy = null;
        if (model.TenancyId.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            tenancy = await _context.Tenancies
                .SingleOrDefaultAsync(candidate =>
                        candidate.Id == model.TenancyId.Value &&
                        candidate.TenantId == userId &&
                        candidate.Status == TenancyStatus.Active &&
                        candidate.StartDate <= today &&
                        (candidate.EndDate == null || candidate.EndDate >= today),
                    cancellationToken);
        }

        if (tenancy is null)
        {
            ModelState.AddModelError(
                nameof(model.TenancyId),
                "Select a tenancy that is currently active for your account.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAvailableTenanciesAsync(model, userId, cancellationToken);
            return View(model);
        }

        var now = DateTime.UtcNow;
        var request = new MaintenanceRequest
        {
            TenancyId = tenancy!.Id,
            Description = description,
            Status = MaintenanceRequestStatus.Submitted,
            DateSubmitted = now,
            LastUpdatedUtc = now
        };

        _context.MaintenanceRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Maintenance request submitted.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    [Authorize(Roles = "Landlord,Admin")]
    public async Task<IActionResult> UpdateStatus(int id, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var request = await ManageableRequests(userId)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (request is null) return NotFound();

        return View(BuildStatusUpdateModel(request));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Landlord,Admin")]
    public async Task<IActionResult> UpdateStatus(
        int id,
        MaintenanceRequestStatusUpdateInputModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var request = await ManageableRequests(userId)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (request is null) return NotFound();

        if (!model.NewStatus.HasValue || !request.Status.CanTransitionTo(model.NewStatus.Value))
        {
            ModelState.AddModelError(
                nameof(model.NewStatus),
                "The selected status transition is not allowed.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = BuildStatusUpdateModel(request);
            invalidModel.NewStatus = model.NewStatus;
            return View(invalidModel);
        }

        request.Status = model.NewStatus!.Value;
        request.LastUpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Maintenance request status updated.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    private IQueryable<MaintenanceRequest> AuthorizedRequests(string userId)
    {
        var requests = _context.MaintenanceRequests
            .Include(request => request.Tenancy)
                .ThenInclude(tenancy => tenancy!.Property)
            .Include(request => request.Tenancy)
                .ThenInclude(tenancy => tenancy!.Tenant)
            .AsQueryable();

        if (User.IsInRole("Admin")) return requests;

        var isLandlord = User.IsInRole("Landlord");
        var isTenant = User.IsInRole("Tenant");

        return requests.Where(request =>
            (isLandlord && request.Tenancy!.Property!.LandlordId == userId) ||
            (isTenant && request.Tenancy!.TenantId == userId));
    }

    private IQueryable<MaintenanceRequest> ManageableRequests(string userId)
    {
        var requests = _context.MaintenanceRequests
            .Include(request => request.Tenancy)
                .ThenInclude(tenancy => tenancy!.Property)
            .Include(request => request.Tenancy)
                .ThenInclude(tenancy => tenancy!.Tenant)
            .AsQueryable();

        return User.IsInRole("Admin")
            ? requests
            : requests.Where(request => request.Tenancy!.Property!.LandlordId == userId);
    }

    private async Task PopulateAvailableTenanciesAsync(
        MaintenanceRequestCreateViewModel model,
        string userId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        model.AvailableTenancies = await _context.Tenancies
            .AsNoTracking()
            .Where(tenancy =>
                tenancy.TenantId == userId &&
                tenancy.Status == TenancyStatus.Active &&
                tenancy.StartDate <= today &&
                (tenancy.EndDate == null || tenancy.EndDate >= today))
            .OrderBy(tenancy => tenancy.Property!.Title)
            .Select(tenancy => new SelectListItem
            {
                Value = tenancy.Id.ToString(),
                Text = tenancy.Property!.Title + " — " + tenancy.Property.Address
            })
            .ToListAsync(cancellationToken);
    }

    private static MaintenanceRequestStatusUpdateViewModel BuildStatusUpdateModel(
        MaintenanceRequest request) =>
        new()
        {
            Id = request.Id,
            PropertyTitle = request.Tenancy!.Property!.Title,
            TenantName = request.Tenancy.Tenant!.FullName,
            CurrentStatus = request.Status,
            AllowedStatuses = request.Status
                .GetAllowedTransitions()
                .Select(status => new SelectListItem
                {
                    Value = status.ToString(),
                    Text = GetStatusDisplayName(status)
                })
                .ToList()
        };

    private static string GetStatusDisplayName(MaintenanceRequestStatus status) =>
        status == MaintenanceRequestStatus.InProgress ? "In Progress" : status.ToString();
}
