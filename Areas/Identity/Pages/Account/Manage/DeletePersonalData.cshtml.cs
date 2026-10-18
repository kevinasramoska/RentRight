using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RentRight.Areas.Identity.Pages.Account.Manage;

[Authorize]
public sealed class DeletePersonalDataModel : PageModel
{
    public void OnGet() { }
    // No self-delete path may bypass retained business history or the last-admin guard.
    public IActionResult OnPost() => BadRequest("Account deletion is not available. Contact the administrator for a reviewed account request.");
}
