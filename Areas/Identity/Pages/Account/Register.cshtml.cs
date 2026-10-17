using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RentRight.Authorization;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class RegisterModel(IWebHostEnvironment environment, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, ApplicationDbContext db) : PageModel
{
    [BindProperty] public RegistrationInput Input { get; set; } = new();
    public IActionResult OnGet() => AccountSecurity.CanSelfRegister(environment) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!AccountSecurity.CanSelfRegister(environment)) return NotFound();
        if (!ModelState.IsValid) return Page();
        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email, FullName = Input.FullName?.Trim() ?? string.Empty };
        var result = await users.CreateAsync(user, Input.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, AppRoles.Tenant);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(HttpContext.RequestAborted);
            ModelState.AddModelError(string.Empty, "Unable to create an account. Check your details and password requirements.");
            return Page();
        }
        await transaction.CommitAsync(HttpContext.RequestAborted);
        await signIn.SignInAsync(user, isPersistent: false);
        return LocalRedirect("/Dashboard");
    }

    public sealed class RegistrationInput
    {
        [StringLength(200), Display(Name = "Full name")] public string? FullName { get; set; }
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
        [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = string.Empty;
    }
}
