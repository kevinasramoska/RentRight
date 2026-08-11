using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using RentRight.ViewModels;

namespace RentRight.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController(IAdministrationService administrationService) : Controller
    {
        // GET: /Admin
        public async Task<IActionResult> Index(
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            var outcome = await administrationService.ListUsersAsync(
                new AdministrationUsersQuery(page),
                cancellationToken);

            return outcome.Status switch
            {
                ListAdministrationUsersStatus.Success => View(ToViewModel(outcome.Page!)),
                ListAdministrationUsersStatus.Unauthenticated => Challenge(),
                ListAdministrationUsersStatus.Forbidden => Forbid(),
                _ => throw new InvalidOperationException("Unknown Administration list outcome.")
            };
        }

        // POST: /Admin/UpdateRoles
        [HttpPost]
        public async Task<IActionResult> UpdateRoles(
            string userId,
            List<string>? selectedRoles,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                AddValidationError(nameof(userId), "A user is required.");
                return RedirectToAction(nameof(Index));
            }

            var outcome = await administrationService.UpdateUserRolesAsync(
                new UpdateUserRolesCommand(userId, selectedRoles ?? []),
                cancellationToken);

            return MapUpdateOutcome(outcome);
        }

        private IActionResult MapUpdateOutcome(UpdateUserRolesOutcome outcome)
        {
            switch (outcome.Status)
            {
                case UpdateUserRolesStatus.Success:
                    TempData["Success"] = $"Updated roles for {outcome.UserEmail}";
                    return RedirectToAction(nameof(Index));
                case UpdateUserRolesStatus.Unauthenticated:
                    return Challenge();
                case UpdateUserRolesStatus.Forbidden:
                    return Forbid();
                case UpdateUserRolesStatus.UserNotFound:
                    return NotFound();
                case UpdateUserRolesStatus.InvalidRoles:
                    AddValidationError(
                        "selectedRoles",
                        "One or more selected roles are invalid.");
                    return RedirectToAction(nameof(Index));
                case UpdateUserRolesStatus.LastAdministrator:
                    AddValidationError(
                        "selectedRoles",
                        "At least one administrator account must remain assigned to the Admin role.");
                    return RedirectToAction(nameof(Index));
                case UpdateUserRolesStatus.IdentityFailure:
                    AddValidationError(
                        "selectedRoles",
                        $"Unable to update roles. {string.Join(" ", outcome.Errors ?? [])}");
                    return RedirectToAction(nameof(Index));
                case UpdateUserRolesStatus.ConcurrentUpdate:
                    AddValidationError(
                        "selectedRoles",
                        "Roles changed concurrently. Reload the page and try again.");
                    return RedirectToAction(nameof(Index));
                default:
                    throw new InvalidOperationException("Unknown Administration update outcome.");
            }
        }

        private void AddValidationError(string key, string message)
        {
            ModelState.AddModelError(key, message);
            TempData["Error"] = message;
        }

        private static AdminUserRolesViewModel ToViewModel(AdministrationUsersPage page) =>
            new()
            {
                Users = page.Users
                    .Select(user => new UserRoleRowViewModel
                    {
                        UserId = user.UserId,
                        FullName = user.FullName,
                        Email = user.Email,
                        Roles = user.Roles
                    })
                    .ToArray(),
                AvailableRoles = page.AvailableRoles,
                Page = page.Page,
                TotalPages = page.TotalPages,
                TotalUsers = page.TotalUsers
            };
    }
}
