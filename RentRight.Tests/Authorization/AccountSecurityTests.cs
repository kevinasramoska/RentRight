using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RentRight.Authorization;
using RentRight.Tests.Api;
using Xunit;

namespace RentRight.Tests.Authorization;

public sealed class AccountSecurityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T12:00:00Z");

    [Theory]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    [InlineData("2026-10-17T12:01:00.0000000+00:00", false)]
    [InlineData("2026-10-17T11:50:00.0000000+00:00", false)]
    [InlineData("2026-10-17T11:51:00.0000000+00:00", true)]
    [InlineData("2026-10-17T12:00:00.0000000+00:00", true)]
    public void IsWithin_MissingFutureMalformedOrExpiredTimestamp_FailsClosed(string? timestamp, bool expected)
    {
        var properties = new AuthenticationProperties();
        properties.Items[AccountSecurity.PasswordVerified] = timestamp;
        Assert.Equal(expected, AccountSecurity.IsWithin(properties, AccountSecurity.PasswordVerified, Now, AccountSecurity.RecentAuthenticationAge));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task Configuration_Environment_HasExplicitIdentityAndCookieRules(string environment, bool registration)
    {
        await using var factory = new RentRightApplicationFactory(environment);
        var identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(12, identity.Password.RequiredLength);
        Assert.Equal(4, identity.Password.RequiredUniqueChars);
        Assert.True(identity.Password.RequireDigit && identity.Password.RequireLowercase && identity.Password.RequireUppercase && identity.Password.RequireNonAlphanumeric);
        Assert.True(identity.User.RequireUniqueEmail);
        Assert.False(identity.SignIn.RequireConfirmedAccount);
        Assert.True(identity.Lockout.AllowedForNewUsers);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(TimeSpan.FromMinutes(30), cookie.ExpireTimeSpan);
        Assert.True(cookie.SlidingExpiration && cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.Equal("/Identity/Account/AccessDenied", cookie.AccessDeniedPath.Value);
        Assert.Equal(registration, AccountSecurity.CanSelfRegister(factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
    }

    [Fact]
    public async Task CookieRefresh_WithoutPasswordVerification_PreservesOriginalSecurityTimes()
    {
        await using var factory = new RentRightApplicationFactory("Development");
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var properties = new AuthenticationProperties();
        properties.Items[AccountSecurity.SessionStarted] = AccountSecurity.Timestamp(Now.AddHours(-4));
        properties.Items[AccountSecurity.PasswordVerified] = AccountSecurity.Timestamp(Now.AddMinutes(-15));
        var context = new CookieSigningInContext(new DefaultHttpContext(),
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            cookie, new ClaimsPrincipal(), properties, new CookieOptions());

        await cookie.Events.OnSigningIn(context);

        Assert.Equal(AccountSecurity.Timestamp(Now.AddHours(-4)), properties.Items[AccountSecurity.SessionStarted]);
        Assert.Equal(AccountSecurity.Timestamp(Now.AddMinutes(-15)), properties.Items[AccountSecurity.PasswordVerified]);
        var missing = new AuthenticationProperties();
        var freshContext = new CookieSigningInContext(new DefaultHttpContext(), context.Scheme, cookie, new ClaimsPrincipal(), missing, new CookieOptions());
        await cookie.Events.OnSigningIn(freshContext);
        Assert.False(missing.Items.ContainsKey(AccountSecurity.PasswordVerified));
        Assert.True(missing.Items.ContainsKey(AccountSecurity.SessionStarted));
    }
}
