using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using RentRight.Models;

namespace RentRight.Authorization;

public static class AccountSecurity
{
    public const string SessionStarted = "rental.session-started";
    public const string PasswordVerified = "rental.password-verified";
    public const string FreshPasswordSignIn = "rental.fresh-password-sign-in";
    public static readonly TimeSpan MaximumSessionAge = TimeSpan.FromHours(8);
    public static readonly TimeSpan RecentAuthenticationAge = TimeSpan.FromMinutes(10);

    public static bool CanSelfRegister(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static string Timestamp(DateTimeOffset now) => now.ToString("O", CultureInfo.InvariantCulture);

    public static bool IsWithin(AuthenticationProperties properties, string key, DateTimeOffset now, TimeSpan age) =>
        properties.Items.TryGetValue(key, out var text) &&
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) &&
        timestamp <= now && now - timestamp < age;

    public static IServiceCollection AddAccountSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAccountSessionValidator, AccountSessionValidator>();
        services.AddScoped<RecentAuthenticationFilter>();
        services.AddScoped<AccountSessionRefreshFilter>();
        services.Configure<RazorPagesOptions>(options =>
        {
            options.Conventions.AddAreaFolderApplicationModelConvention("Identity", "/Account/Manage", model =>
            {
                model.Filters.Add(new ServiceFilterAttribute(typeof(RecentAuthenticationFilter)));
                model.Filters.Add(new ServiceFilterAttribute(typeof(AccountSessionRefreshFilter)));
            });
        });
        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.User.RequireUniqueEmail = true;
            // No email delivery exists yet: production self-registration is disabled instead.
            options.SignIn.RequireConfirmedAccount = false;
        });
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, time) =>
            {
                options.TimeProvider = time;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;
                options.LoginPath = "/Identity/Account/Login";
                options.AccessDeniedPath = "/Identity/Account/AccessDenied";
                options.Events.OnSigningIn = context =>
                {
                    context.Properties.Items.TryAdd(SessionStarted, Timestamp(time.GetUtcNow()));
                    // Refresh/sliding renewal must never manufacture recent password verification.
                    if (context.HttpContext.Items.ContainsKey(FreshPasswordSignIn))
                        context.Properties.Items[PasswordVerified] = Timestamp(time.GetUtcNow());
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices
                    .GetRequiredService<IAccountSessionValidator>().ValidateAsync(context);
            });
        return services;
    }
}

public interface IAccountSessionValidator
{
    Task ValidateAsync(CookieValidatePrincipalContext context);
}

public sealed class AccountSessionValidator(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    TimeProvider time) : IAccountSessionValidator
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        // Check every authenticated request, not only after Identity's usual interval.
        var user = await signIn.ValidateSecurityStampAsync(context.Principal);
        if (user is not null && !await users.IsLockedOutAsync(user) && await signIn.CanSignInAsync(user) &&
            AccountSecurity.IsWithin(context.Properties, AccountSecurity.SessionStarted, time.GetUtcNow(), AccountSecurity.MaximumSessionAge))
            return;

        context.RejectPrincipal();
        await signIn.SignOutAsync();
    }
}

public sealed class RecentAuthenticationFilter(TimeProvider time) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is PageActionDescriptor page)
        {
            // Basic authenticated account navigation stays usable; self-deletion always
            // reaches the explicit denial handler. Everything else defaults to step-up.
            if (page.ViewEnginePath == "/Account/Manage/DeletePersonalData") return;
            if ((HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method)) &&
                page.ViewEnginePath is "/Account/Manage/Index" or "/Account/Manage/PersonalData" or "/Account/Manage/TwoFactorAuthentication")
                return;
        }
        var authentication = await context.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (authentication.Succeeded && authentication.Properties is not null &&
            AccountSecurity.IsWithin(authentication.Properties, AccountSecurity.PasswordVerified,
                time.GetUtcNow(), AccountSecurity.RecentAuthenticationAge)) return;

        // Never replay a sensitive POST automatically after reauthentication.
        var returnUrl = context.ActionDescriptor is PageActionDescriptor ? "/Identity/Account/Manage" : "/Admin";
        context.Result = new RedirectToPageResult("/Account/Reauthenticate", new { area = "Identity", returnUrl });
    }
}

// Some framework account handlers rotate the stamp without refreshing the current
// cookie (including initial authenticator-key creation). Preserve that legitimate
// recent-password journey without accepting an externally changed/unpersisted stamp.
public sealed class AccountSessionRefreshFilter(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var user = await users.GetUserAsync(context.HttpContext.User);
        var originalStamp = user?.SecurityStamp;
        var executed = await next();
        if (executed.Exception is not null || executed.Canceled || user is null || user.SecurityStamp == originalStamp ||
            context.HttpContext.User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) == user.SecurityStamp)
            return;

        var persistedStamp = await users.Users.AsNoTracking().Where(item => item.Id == user.Id)
            .Select(item => item.SecurityStamp).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (persistedStamp == user.SecurityStamp)
            await signIn.RefreshSignInAsync(user);
    }
}
