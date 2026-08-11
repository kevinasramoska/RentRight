using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RentRight.Models;
using RentRight.ViewModels;

namespace RentRight.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            var users = _userManager.Users.OrderBy(user => user.Email).ToList();
            var allRoles = _roleManager.Roles
                .Select(role => role.Name!)
                .OrderBy(role => role)
                .ToList();

            var userRoles = new List<UserRoleRowViewModel>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userRoles.Add(new UserRoleRowViewModel
                {
                    User = user,
                    Roles = roles.ToList()
                });
            }

            return View(new AdminUserRolesViewModel
            {
                Users = userRoles,
                AvailableRoles = allRoles
            });
        }

        // POST: /Admin/UpdateRoles
        [HttpPost]
        public async Task<IActionResult> UpdateRoles(string userId, List<string>? selectedRoles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var requestedRoles = (selectedRoles ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var validRoles = _roleManager.Roles
                .Select(role => role.Name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requestedRoles.Any(role => !validRoles.Contains(role)))
            {
                TempData["Error"] = "One or more selected roles are invalid.";
                return RedirectToAction(nameof(Index));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Contains("Admin") && !requestedRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            {
                var administrators = await _userManager.GetUsersInRoleAsync("Admin");
                if (administrators.Count == 1)
                {
                    TempData["Error"] = "At least one administrator account must remain assigned to the Admin role.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var rolesToAdd = requestedRoles.Except(currentRoles);
            var rolesToRemove = currentRoles.Except(requestedRoles);

            if (rolesToAdd.Any())
                await _userManager.AddToRolesAsync(user, rolesToAdd);

            if (rolesToRemove.Any())
                await _userManager.RemoveFromRolesAsync(user, rolesToRemove);

            TempData["Success"] = $"Updated roles for {user.Email}";
            return RedirectToAction(nameof(Index));
        }
    }
}
