using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RentRight.Authorization;
using RentRight.Models;

namespace RentRight.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<ApplicationUser> signIn, IWebHostEnvironment environment) : PageModel
{
    [BindProperty] public LoginInput Input { get; set; } = new();
    public string ReturnUrl { get; private set; } = "/";
    public bool CanRegister => AccountSecurity.CanSelfRegister(environment);

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (!ModelState.IsValid) return Page();
        HttpContext.Items[AccountSecurity.FreshPasswordSignIn] = true;
        var result = await signIn.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(ReturnUrl);
        if (result.RequiresTwoFactor)
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl, Input.RememberMe });
        // Use one message for missing, locked and invalid credentials.
        ModelState.AddModelError(string.Empty, "Unable to sign in. Check your details or try again later.");
        return Page();
    }

    public sealed class LoginInput
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }
}
