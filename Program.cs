using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentRight.Application;
using RentRight.Data;
using RentRight.Features.Administration;
using RentRight.Features.Maintenance;
using RentRight.Features.Properties;
using RentRight.Features.Tenancies;
using RentRight.Models;

var builder = WebApplication.CreateBuilder(args);

// -----------------
// Database setup
// -----------------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// -----------------
// Identity setup (with UI + Roles)
// -----------------
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddDefaultUI(); // ✅ enables /Identity/Account/Login etc.

builder.Services.AddApplicationFoundation();
builder.Services.AddAdministrationFeature();
builder.Services.AddPropertiesFeature();
builder.Services.AddTenanciesAndDashboardsFeature();
builder.Services.AddMaintenanceFeature();
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRazorPages(); // ✅ needed for Identity UI

var app = builder.Build();

// Seed roles and, in development, an optional administrator configured outside source control.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Admin", "Landlord", "Tenant" };

    // Create roles if they don't exist
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var createRoleResult = await roleManager.CreateAsync(new IdentityRole(role));
            EnsureIdentitySucceeded(createRoleResult, $"create the {role} role");
        }
    }

    var adminEmail = builder.Configuration["SeedAdmin:Email"];
    var adminPassword = builder.Configuration["SeedAdmin:Password"];

    if (app.Environment.IsDevelopment() &&
        !string.IsNullOrWhiteSpace(adminEmail) &&
        !string.IsNullOrWhiteSpace(adminPassword))
    {
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            var newAdmin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = "Admin User"
            };

            var createAdminResult = await userManager.CreateAsync(newAdmin, adminPassword);
            EnsureIdentitySucceeded(createAdminResult, "create the configured development administrator");

            var addAdminRoleResult = await userManager.AddToRoleAsync(newAdmin, "Admin");
            EnsureIdentitySucceeded(addAdminRoleResult, "assign the Admin role to the development administrator");
        }
    }
}

// -----------------
// Middleware pipeline
// -----------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// -----------------
// Routes
// -----------------
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // ✅ enables Identity UI routing

app.Run();

static void EnsureIdentitySucceeded(IdentityResult result, string operation)
{
    if (result.Succeeded)
    {
        return;
    }

    var errors = string.Join(
        "; ",
        result.Errors.Select(error => $"{error.Code}: {error.Description}"));
    throw new InvalidOperationException($"Unable to {operation}. {errors}");
}
