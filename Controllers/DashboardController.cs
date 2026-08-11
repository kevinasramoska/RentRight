using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;
using Microsoft.AspNetCore.Identity;
using RentRight.ViewModels;

namespace RentRight.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("Admin"))
                return RedirectToAction(nameof(AdminDashboard));

            if (roles.Contains("Landlord"))
                return RedirectToAction(nameof(LandlordDashboard));

            if (roles.Contains("Tenant"))
                return RedirectToAction(nameof(TenantDashboard));

            return View("GenericDashboard");
        }

        // --- ADMIN DASHBOARD ---
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminDashboard()
        {
            var totalUsers = await _context.Users.CountAsync();
            var totalProperties = await _context.Properties.CountAsync();
            return View("AdminDashboard", new AdminDashboardViewModel
            {
                TotalUsers = totalUsers,
                TotalProperties = totalProperties
            });
        }

        // --- LANDLORD DASHBOARD ---
        [Authorize(Roles = "Landlord")]
        public async Task<IActionResult> LandlordDashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var myProperties = await _context.Properties
                .Where(p => p.LandlordId == user.Id)
                .ToListAsync();

            return View("LandlordDashboard", myProperties);
        }

        // --- TENANT DASHBOARD ---
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> TenantDashboard(CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Challenge();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var tenancies = await _context.Tenancies
                .AsNoTracking()
                .Where(tenancy => tenancy.TenantId == userId)
                .OrderByDescending(tenancy => tenancy.Status == TenancyStatus.Active)
                .ThenByDescending(tenancy => tenancy.StartDate)
                .Select(tenancy => new TenantTenancySummaryViewModel
                {
                    Id = tenancy.Id,
                    PropertyTitle = tenancy.Property!.Title,
                    PropertyAddress = tenancy.Property.Address,
                    StartDate = tenancy.StartDate,
                    EndDate = tenancy.EndDate,
                    Status = tenancy.Status,
                    IsCurrentlyActive = tenancy.Status == TenancyStatus.Active &&
                        tenancy.StartDate <= today &&
                        (tenancy.EndDate == null || tenancy.EndDate >= today)
                })
                .ToListAsync(cancellationToken);

            var recentRequests = await _context.MaintenanceRequests
                .AsNoTracking()
                .Where(request => request.Tenancy!.TenantId == userId)
                .OrderByDescending(request => request.DateSubmitted)
                .Take(5)
                .Select(request => new MaintenanceRequestRowViewModel
                {
                    Id = request.Id,
                    PropertyTitle = request.Tenancy!.Property!.Title,
                    TenantName = request.Tenancy.Tenant!.FullName,
                    Description = request.Description,
                    DateSubmitted = request.DateSubmitted,
                    Status = request.Status,
                    CanUpdateStatus = false
                })
                .ToListAsync(cancellationToken);

            return View(new TenantDashboardViewModel
            {
                Tenancies = tenancies,
                RecentMaintenanceRequests = recentRequests
            });
        }
    }
}
