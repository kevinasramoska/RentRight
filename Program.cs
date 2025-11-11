using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
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

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // ✅ needed for Identity UI

var app = builder.Build();

// -----------------
// Seed Roles + Admin User
// -----------------
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
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Create default admin user
    string adminEmail = "admin@rentright.com";
    string adminPassword = "Admin!123";

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

        var createAdmin = await userManager.CreateAsync(newAdmin, adminPassword);
        if (createAdmin.Succeeded)
            await userManager.AddToRoleAsync(newAdmin, "Admin");
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
