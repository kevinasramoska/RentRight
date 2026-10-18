using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RentRight.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class RegisterConfirmationModel : PageModel
{
    // The default UI can display a confirmation token when its email sender is a no-op.
    // No account may self-confirm through a demo link while delivery is unconfigured.
    public IActionResult OnGet() => NotFound();
    public IActionResult OnPost() => NotFound();
}
