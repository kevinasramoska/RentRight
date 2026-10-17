using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Authorization;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

[Trait("Category", "SqlIntegration")]
public sealed class AccountSecuritySqlTests : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private readonly SqlApplication _app;
    private readonly SqlServerFixture _server;
    private readonly TestTime _clock = new();
    private const string RoleRoute = "/Admin/UpdateRoles";
    private const string Reauthenticate = "/Identity/Account/Reauthenticate";
    private const string Register = "/Identity/Account/Register";
    private readonly string _newPassword = $"T!{Guid.NewGuid():N}a9";

    public AccountSecuritySqlTests(SqlServerFixture server)
    {
        _server = server;
        _app = new(server.ConnectionString) { Clock = _clock };
    }
    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    [Theory]
    [InlineData(Actor.Tenant)]
    [InlineData(Actor.OwningLandlord)]
    [InlineData(Actor.Admin)]
    [InlineData(Actor.MultiRoleAdmin)]
    public async Task Session_PermanentlyLockedAccount_RejectsExistingCookieImmediately(Actor actor)
    {
        using var client = await _app.CreateActorClientAsync(actor);
        await UsersAsync(async users =>
        {
            var user = await users.FindByIdAsync(actor.ToString());
            Assert.True((await users.SetLockoutEndDateAsync(user!, DateTimeOffset.MaxValue)).Succeeded);
        });

        using var response = await client.GetAsync("/api/v1/tenancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var session = await client.GetAsync("/api/v1/session");
        using var json = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Session_SecurityStampChanged_RejectsExistingCookie()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Tenant);
        await UsersAsync(async users => Assert.True((await users.UpdateSecurityStampAsync((await users.FindByIdAsync("Tenant"))!)).Succeeded));
        using var response = await client.GetAsync("/api/v1/tenancies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Session_IdleExpiry_RequiresLoginWithoutApiRedirect()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Tenant);
        _clock.Advance(TimeSpan.FromMinutes(31));

        using var response = await client.GetAsync("/api/v1/tenancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Session_ActiveSlidingRenewals_CannotExceedAbsoluteLifetime()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Tenant);
        for (var interval = 1; interval < 24; interval++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            using var activity = await client.GetAsync("/api/v1/tenancies");
            Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
            Assert.Contains(activity.Headers.GetValues("Set-Cookie"), value =>
                value.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase));
        }
        _clock.Advance(TimeSpan.FromMinutes(20));
        using var expired = await client.GetAsync("/api/v1/tenancies");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Login_FiveBadPasswords_LocksAccountAndDoesNotEnumerateUsers()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        string? invalidMessage = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostFormAsync(client, "/Identity/Account/Login", new()
            { ["Input.Email"] = "Tenant@example.test", ["Input.Password"] = "Wrong-password-93!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            invalidMessage = "Unable to sign in. Check your details or try again later.";
            Assert.Contains(invalidMessage, await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain("Wrong-password-93!", await response.Content.ReadAsStringAsync());
        }
        await UsersAsync(async users => Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync("Tenant"))!)));
        using var unknown = await PostFormAsync(client, "/Identity/Account/Login", new()
        { ["Input.Email"] = "nobody@example.test", ["Input.Password"] = "Wrong-password-93!" });
        Assert.Contains(invalidMessage!, await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_Development_ForgedRolesAndConfirmationCannotEscalateTenant()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        var fields = Registration();
        fields["Input.Roles"] = "Admin";
        fields["SelectedRoles"] = "Landlord";
        fields["Input.EmailConfirmed"] = "true";
        using var response = await PostFormAsync(client, Register, fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await UsersAsync(async users =>
        {
            var user = await users.FindByEmailAsync(fields["Input.Email"]);
            Assert.NotNull(user);
            Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync(user));
            Assert.False(user.EmailConfirmed);
        });
        using var admin = await client.GetAsync("/Admin");
        Assert.EndsWith("/Identity/Account/AccessDenied?ReturnUrl=%2FAdmin", admin.Headers.Location!.OriginalString);
        using var tenant = await client.GetAsync("/api/v1/tenant-dashboard");
        Assert.Equal(HttpStatusCode.OK, tenant.StatusCode);
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("aaaaaaaaaaaa")]
    public async Task Register_WeakPassword_DoesNotCreateUser(string password)
    {
        using var client = await _app.CreateActorClientAsync(Actor.Anonymous);
        var fields = Registration();
        fields["Input.Password"] = fields["Input.ConfirmPassword"] = password;
        using var response = await PostFormAsync(client, Register, fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await UsersAsync(async users => Assert.Null(await users.FindByEmailAsync(fields["Input.Email"])));
    }

    [Fact]
    public async Task Register_Production_DirectGetAndPostAreClosedWithoutCreatingAccount()
    {
        await using var production = new SqlApplication(_server.ConnectionString) { EnvironmentName = "Production" };
        await production.InitializeAsync();
        using var client = await production.CreateActorClientAsync(Actor.Anonymous);
        using var page = await client.GetAsync(Register);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        // An anonymous login form supplies a real antiforgery token for the direct POST.
        using var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("href=\"/Identity/Account/Register", html);
        var fields = Registration();
        fields["__RequestVerificationToken"] = Token(html);
        using var post = await client.PostAsync(Register, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        using var scope = production.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(fields["Input.Email"]));
        // Existing provisioned local accounts still have a usable sign-in, with no email-confirmation dead end.
        using var existing = await production.CreateActorClientAsync(Actor.Admin);
        using var admin = await existing.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Theory]
    [InlineData(Actor.Admin)]
    [InlineData(Actor.Tenant)]
    public async Task Account_SelfDeletionCannotBypassHistoryOrLastAdminGuard(Actor actor)
    {
        using var client = await _app.CreateActorClientAsync(actor);
        await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await client.PostAsync("/Identity/Account/Manage/DeletePersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await UsersAsync(async users => Assert.NotNull(await users.FindByIdAsync(actor.ToString())));
    }

    [Fact]
    public async Task Roles_StaleAuthentication_RequiresPasswordThenExplicitResubmission()
    {
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        _clock.Advance(TimeSpan.FromMinutes(11));
        using var blocked = await RolesAsync(admin, "OwningLandlord", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.StartsWith(Reauthenticate, blocked.Headers.Location!.OriginalString);
        await UsersAsync(async users => Assert.Equal(new[] { "Landlord" }, await users.GetRolesAsync((await users.FindByIdAsync("OwningLandlord"))!)));

        // Reauthentication targets the signed-in account; a submitted other user's ID is ignored.
        using var bad = await PostFormAsync(admin, Reauthenticate + "?returnUrl=https://example.test", new()
        { ["Password"] = "Wrong-password-93!", ["UserId"] = "OwningLandlord" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        using var stillBlocked = await RolesAsync(admin, "OwningLandlord", "Tenant");
        Assert.StartsWith(Reauthenticate, stillBlocked.Headers.Location!.OriginalString);
        // Give this local fixture admin a known password through Identity, then sign in afresh
        // and age that login so the success path exercises the actual password form.
        await UsersAsync(async users =>
        {
            var user = (await users.FindByIdAsync("Admin"))!;
            Assert.True((await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), _newPassword)).Succeeded);
        });
        using var fresh = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var signedIn = await PostFormAsync(fresh, "/Identity/Account/Login", new()
        { ["Input.Email"] = "Admin@example.test", ["Input.Password"] = _newPassword });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        await SqlApplication.AddAntiforgeryAsync(fresh);
        _clock.Advance(TimeSpan.FromMinutes(11));
        using var verified = await PostFormAsync(fresh, Reauthenticate + "?returnUrl=https://example.test", new() { ["Password"] = _newPassword });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Equal("/Admin", verified.Headers.Location!.OriginalString);
        await UsersAsync(async users => Assert.Equal(new[] { "Landlord" }, await users.GetRolesAsync((await users.FindByIdAsync("OwningLandlord"))!)));
        await SqlApplication.AddAntiforgeryAsync(fresh);
        using var retried = await RolesAsync(fresh, "OwningLandlord", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, retried.StatusCode);
        await UsersAsync(async users => Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync((await users.FindByIdAsync("OwningLandlord"))!)));
    }

    [Fact]
    public async Task Roles_Change_InvalidatesTargetCookieAndReloadsLocalRolesOnNewLogin()
    {
        using var target = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        using var change = await RolesAsync(admin, "OwningLandlord", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        using var stale = await target.GetAsync("/api/v1/landlord-maintenance-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        using var renewed = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        using var denied = await renewed.GetAsync("/api/v1/landlord-maintenance-requests");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var tenant = await renewed.GetAsync("/api/v1/tenant-dashboard");
        Assert.Equal(HttpStatusCode.OK, tenant.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roles_ConcurrentDemotions_AlwaysRetainOneUsableAdministrator(bool demoteOther)
    {
        using var first = await _app.CreateActorClientAsync(Actor.Admin);
        using var second = await _app.CreateActorClientAsync(Actor.MultiRoleAdmin);
        await SqlApplication.AddAntiforgeryAsync(first);
        await SqlApplication.AddAntiforgeryAsync(second);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Demote(HttpClient client, string target)
        { await gate.Task; return await RolesAsync(client, target, "Tenant"); }
        var a = Demote(first, demoteOther ? "MultiRoleAdmin" : "Admin");
        var b = Demote(second, demoteOther ? "Admin" : "MultiRoleAdmin");
        gate.SetResult();
        var responses = await Task.WhenAll(a, b);
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
        await UsersAsync(async users =>
        {
            var administrators = await users.GetUsersInRoleAsync("Admin");
            Assert.Single(administrators);
            Assert.False(await users.IsLockedOutAsync(administrators[0]));
            Assert.True(await users.HasPasswordAsync(administrators[0]));
            var loser = (await users.FindByIdAsync(administrators[0].Id == "Admin" ? "MultiRoleAdmin" : "Admin"))!;
            Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync(loser));
        });
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("passwordless")]
    public async Task Roles_UnusableOtherAdmin_DoesNotPermitLastUsableAdminDemotion(string scenario)
    {
        await UsersAsync(async users =>
        {
            var other = (await users.FindByIdAsync("MultiRoleAdmin"))!;
            if (scenario == "locked") Assert.True((await users.SetLockoutEndDateAsync(other, DateTimeOffset.MaxValue)).Succeeded);
            else Assert.True((await users.RemovePasswordAsync(other)).Succeeded);
        });
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        using var attempt = await RolesAsync(admin, "Admin", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, attempt.StatusCode);
        await UsersAsync(async users => Assert.Contains("Admin", await users.GetRolesAsync((await users.FindByIdAsync("Admin"))!)));
        using var page = await admin.GetAsync("/Admin");
        Assert.Contains("At least one administrator", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Roles_UnsupportedStoredRole_IsNotOfferedOrAssignable()
    {
        using (var scope = _app.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new("SuperAdmin"))).Succeeded);
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        using var page = await admin.GetAsync("/Admin");
        Assert.DoesNotContain("value=\"SuperAdmin\"", await page.Content.ReadAsStringAsync());
        using var attempt = await RolesAsync(admin, "Tenant", "SuperAdmin");
        Assert.Equal(HttpStatusCode.Redirect, attempt.StatusCode);
        await UsersAsync(async users => Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync((await users.FindByIdAsync("Tenant"))!)));
    }

    [Fact]
    public async Task Roles_LockTimeout_ReturnsRecoverableOutcomeWithoutPartialChanges()
    {
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        await using var db = _app.CreateContext();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_getapplock @Resource=N'RentRight.Identity.RoleAdministration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0;");
            using var blocked = await RolesAsync(admin, "OwningLandlord", "Tenant");
            Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        }
        await UsersAsync(async users => Assert.Equal(new[] { "Landlord" }, await users.GetRolesAsync((await users.FindByIdAsync("OwningLandlord"))!)));
        using var page = await admin.GetAsync("/Admin");
        Assert.Contains("Roles changed concurrently", await page.Content.ReadAsStringAsync());
        using var retry = await RolesAsync(admin, "OwningLandlord", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        await UsersAsync(async users => Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync((await users.FindByIdAsync("OwningLandlord"))!)));
    }

    [Fact]
    public async Task Roles_StampWriteFailure_RollsBackRoleChangesAndStamp()
    {
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        await SqlApplication.AddAntiforgeryAsync(admin);
        await using var db = _app.CreateContext();
        var before = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == "OwningLandlord")).SecurityStamp;
        // A constraint permits the original stamp during Add/RemoveToRoles but rejects only
        // the later stamp rotation. An UPDATE trigger would instead trip EF's OUTPUT restriction.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @ddl nvarchar(max) = N'ALTER TABLE AspNetUsers ADD CONSTRAINT CK_Test_RejectStamp CHECK (Id<>N''OwningLandlord'' OR SecurityStamp=' + QUOTENAME({before}, '''') + N')';
            EXEC(@ddl);
            """);
        using var response = await RolesAsync(admin, "OwningLandlord", "Tenant");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await UsersAsync(async users =>
        {
            var user = (await users.FindByIdAsync("OwningLandlord"))!;
            Assert.Equal(before, user.SecurityStamp);
            Assert.Equal(new[] { "Landlord" }, await users.GetRolesAsync(user));
        });
    }

    [Fact]
    public async Task Account_UnsafeFormsWithoutAntiforgery_AreRejected()
    {
        using var anonymous = await _app.CreateActorClientAsync(Actor.Anonymous);
        using var registration = await anonymous.PostAsync(Register, new FormUrlEncodedContent(Registration()));
        Assert.Equal(HttpStatusCode.BadRequest, registration.StatusCode);
        using var admin = await _app.CreateActorClientAsync(Actor.Admin);
        using var role = await RolesAsync(admin, "Tenant", "Admin");
        using var verification = await admin.PostAsync(Reauthenticate, new FormUrlEncodedContent(new Dictionary<string, string> { ["Password"] = _newPassword }));
        Assert.Equal(HttpStatusCode.BadRequest, role.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, verification.StatusCode);
        await UsersAsync(async users => Assert.Equal(new[] { "Tenant" }, await users.GetRolesAsync((await users.FindByIdAsync("Tenant"))!)));
    }

    private Task UsersAsync(Func<UserManager<ApplicationUser>, Task> action)
    {
        return ExecuteAsync();
        async Task ExecuteAsync()
        {
            using var scope = _app.Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        }
    }

    [Theory]
    [InlineData(Actor.Tenant)]
    [InlineData(Actor.Admin)]
    public async Task Account_SensitiveIdentityPages_RequireRecentPasswordWithoutExposingOrCreatingKeys(Actor actor)
    {
        using var client = await _app.CreateActorClientAsync(actor);
        await SqlApplication.AddAntiforgeryAsync(client);
        _clock.Advance(TimeSpan.FromMinutes(11));
        foreach (var overview in new[] { "", "/PersonalData", "/TwoFactorAuthentication", "/DeletePersonalData" })
        {
            using var navigation = await client.GetAsync("/Identity/Account/Manage" + overview);
            Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        }
        foreach (var page in new[] { "ChangePassword", "SetPassword", "Email", "ExternalLogins", "EnableAuthenticator",
            "ResetAuthenticator", "Disable2fa", "GenerateRecoveryCodes", "ShowRecoveryCodes" })
        {
            using var response = await client.GetAsync("/Identity/Account/Manage/" + page);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith(Reauthenticate, response.Headers.Location!.OriginalString);
            Assert.Contains("returnUrl=%2FIdentity%2FAccount%2FManage", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
        using var download = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, download.StatusCode);
        Assert.StartsWith(Reauthenticate, download.Headers.Location!.OriginalString);
        using var profileChange = await client.PostAsync("/Identity/Account/Manage", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Input.PhoneNumber"] = "+353000000000" }));
        Assert.Equal(HttpStatusCode.Redirect, profileChange.StatusCode);
        Assert.StartsWith(Reauthenticate, profileChange.Headers.Location!.OriginalString);
        await UsersAsync(async users =>
        {
            var user = (await users.FindByIdAsync(actor.ToString()))!;
            Assert.Null(await users.GetAuthenticatorKeyAsync(user));
            Assert.Equal(0, await users.CountRecoveryCodesAsync(user));
            Assert.Null(user.PhoneNumber);
        });
    }

    [Fact]
    public async Task Account_FreshPassword_AllowsAuthenticatorSetupAndProtectedPersonalDataDownload()
    {
        using var client = await _app.CreateActorClientAsync(Actor.Tenant);
        using var otherSession = await _app.CreateActorClientAsync(Actor.Tenant);
        await SqlApplication.AddAntiforgeryAsync(client);
        _clock.Advance(TimeSpan.FromMinutes(5));
        using var setup = await client.GetAsync("/Identity/Account/Manage/EnableAuthenticator");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        await UsersAsync(async users => Assert.NotNull(await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync("Tenant"))!)));
        using var revoked = await otherSession.GetAsync("/api/v1/tenancies");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var download = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        _clock.Advance(TimeSpan.FromMinutes(6));
        using var noManufacturedRecency = await client.GetAsync("/Identity/Account/Manage/ChangePassword");
        Assert.Equal(HttpStatusCode.Redirect, noManufacturedRecency.StatusCode);
        Assert.StartsWith(Reauthenticate, noManufacturedRecency.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task RegisterConfirmation_NoEmailSender_CannotExposeConfirmationTokenOrUserExistence(string environment)
    {
        await using var app = new SqlApplication(_server.ConnectionString) { EnvironmentName = environment };
        await app.InitializeAsync();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var tenant = (await users.FindByIdAsync("Tenant"))!;
            tenant.EmailConfirmed = false;
            Assert.True((await users.UpdateAsync(tenant)).Succeeded);
        }
        using var client = await app.CreateActorClientAsync(Actor.Anonymous);
        foreach (var email in new[] { "Tenant@example.test", "missing@example.test" })
        {
            using var response = await client.GetAsync("/Identity/Account/RegisterConfirmation?email=" + Uri.EscapeDataString(email));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("code=", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        }
        using var login = await client.GetAsync("/Identity/Account/Login");
        using var post = await client.PostAsync("/Identity/Account/RegisterConfirmation", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["email"] = "Tenant@example.test", ["__RequestVerificationToken"] = Token(await login.Content.ReadAsStringAsync()) }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        using var verify = app.Services.CreateScope();
        Assert.False((await verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync("Tenant"))!.EmailConfirmed);
    }

    [Theory]
    [InlineData(Actor.Anonymous, "/Identity/Account/Login", false)]
    [InlineData(Actor.Tenant, "/Identity/Account/AccessDenied", false)]
    [InlineData(Actor.OwningLandlord, "/Identity/Account/AccessDenied", false)]
    [InlineData(Actor.OtherLandlord, "/Identity/Account/AccessDenied", false)]
    [InlineData(Actor.Admin, "/Admin", true)]
    [InlineData(Actor.MultiRoleAdmin, "/Admin", true)]
    public async Task Roles_HttpActorMatrix_DeniesPrivilegeEscalationExceptAdmin(Actor actor, string redirect, bool permitted)
    {
        using var client = await _app.CreateActorClientAsync(actor);
        if (actor == Actor.Anonymous)
        {
            using var page = await client.GetAsync("/Identity/Account/Login");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Token(await page.Content.ReadAsStringAsync()));
        }
        else await SqlApplication.AddAntiforgeryAsync(client);
        using var response = await RolesAsync(client, "Tenant", "Landlord");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(redirect, new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        await UsersAsync(async users => Assert.Equal(new[] { permitted ? "Landlord" : "Tenant" },
            await users.GetRolesAsync((await users.FindByIdAsync("Tenant"))!)));
    }

    [Fact]
    public async Task Property_LockedOwnerWithPreviouslyValidAntiforgery_CannotMutate()
    {
        using var owner = await _app.CreateActorClientAsync(Actor.OwningLandlord);
        await SqlApplication.AddAntiforgeryAsync(owner);
        await UsersAsync(async users => Assert.True((await users.SetLockoutEndDateAsync(
            (await users.FindByIdAsync("OwningLandlord"))!, DateTimeOffset.MaxValue)).Succeeded));
        using var response = await owner.PostAsJsonAsync("/api/v1/properties", new
        { title = "Blocked mutation", address = "Demo street", rentAmount = 1000 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var db = _app.CreateContext();
        Assert.Empty(await db.Properties.ToArrayAsync());
    }
    private Dictionary<string, string> Registration() => new()
    { ["Input.Email"] = $"new-{Guid.NewGuid():N}@example.test", ["Input.FullName"] = "Demo Resident", ["Input.Password"] = _newPassword, ["Input.ConfirmPassword"] = _newPassword };

    private static Task<HttpResponseMessage> RolesAsync(HttpClient client, string userId, string role) =>
        client.PostAsync(RoleRoute, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["userId"] = userId, ["selectedRoles"] = role }));

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        using var page = await client.GetAsync(path);
        page.EnsureSuccessStatusCode();
        fields["__RequestVerificationToken"] = Token(await page.Content.ReadAsStringAsync());
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
