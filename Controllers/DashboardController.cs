using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;
using Microsoft.AspNetCore.Identity;

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
            return View("AdminDashboard", (totalUsers, totalProperties));
        }

        // --- LANDLORD DASHBOARD ---
        [Authorize(Roles = "Landlord")]
        public async Task<IActionResult> LandlordDashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            var myProperties = await _context.Properties
                .Where(p => p.LandlordId == user.Id)
                .ToListAsync();

            return View("LandlordDashboard", myProperties);
        }

        // --- TENANT DASHBOARD ---
        [Authorize(Roles = "Tenant")]
        public IActionResult TenantDashboard()
        {
            return View();
        }
    }
}
