using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RentRight.Authorization;
using RentRight.Models;

namespace RentRight.Areas.Identity.Pages.Account;

[Authorize]
public sealed class ReauthenticateModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty, Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    public string ReturnUrl { get; private set; } = "/Admin";
    public void OnGet(string? returnUrl = null) => ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Admin";

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        OnGet(returnUrl);
        if (!ModelState.IsValid) return Page();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await signIn.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Unable to verify your password. Try again later if your account is locked.");
            return Page();
        }
        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        HttpContext.Items[AccountSecurity.FreshPasswordSignIn] = true;
        await signIn.SignInAsync(user, authentication.Properties ?? new AuthenticationProperties());
        return LocalRedirect(ReturnUrl);
    }
}
