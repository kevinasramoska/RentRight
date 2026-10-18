using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RentRight.Authorization;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using Xunit;

namespace RentRight.Tests.Api;

public sealed class WorkspaceApiTests
{
    [Fact]
    public async Task OpenApi_WorkspaceProjectionsAndLogout_DeclareResponseContracts()
    {
        await using var factory = Factory(new Administration());
        using var client = Client(factory, "");
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/workspace").GetProperty("get").GetProperty("responses").GetProperty("200").TryGetProperty("content", out _));
        Assert.True(paths.GetProperty("/api/v1/admin-users/roles").GetProperty("put").GetProperty("responses").TryGetProperty("204", out _));
        Assert.True(paths.GetProperty("/api/v1/session/logout").GetProperty("post").GetProperty("responses").TryGetProperty("204", out _));
    }

    [Theory]
    [InlineData("", 401)]
    [InlineData("Tenant", 403)]
    [InlineData("Landlord", 403)]
    [InlineData("Admin", 200)]
    public async Task AdminUsers_RoleMatrix_DeniesBeforeListing(string role, int expected)
    {
        var service = new Administration();
        await using var factory = Factory(service);
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/admin-users");
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(expected == 200 ? 1 : 0, service.Reads);
        if (expected == 200) Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData(UpdateUserRolesStatus.Success, 204)]
    [InlineData(UpdateUserRolesStatus.InvalidRoles, 400)]
    [InlineData(UpdateUserRolesStatus.LastAdministrator, 409)]
    [InlineData(UpdateUserRolesStatus.ConcurrentUpdate, 409)]
    [InlineData(UpdateUserRolesStatus.UserNotFound, 404)]
    [InlineData(UpdateUserRolesStatus.Forbidden, 403)]
    [InlineData(UpdateUserRolesStatus.IdentityFailure, 503)]
    public async Task UpdateRoles_ServiceOutcome_ReturnsSafeProblemOrSuccess(UpdateUserRolesStatus status, int expected)
    {
        var service = new Administration { Status = status };
        await using var factory = Factory(service);
        using var client = Client(factory, "Admin");
        await Csrf(client);
        using var response = await client.PutAsJsonAsync("/api/v1/admin-users/roles", new { userId = "target", selectedRoles = new[] { "Landlord" }, fullName = "forged", securityStamp = "forged" });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("target", service.Command!.UserId);
        Assert.Equal(["Landlord"], service.Command.SelectedRoles);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", body);
        Assert.DoesNotContain("forged", body);
        if (expected != 204) Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateRoles_MissingCsrfOrStaleVerification_DoesNotInvokeService(bool stale)
    {
        var service = new Administration();
        await using var factory = Factory(service);
        using var client = Client(factory, "Admin", recent: !stale);
        if (stale) await Csrf(client);
        using var response = await client.PutAsJsonAsync("/api/v1/admin-users/roles", new { userId = "target", selectedRoles = new[] { "Landlord" } });
        Assert.Equal(stale ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.Command);
        if (stale)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("reauthentication_required", json.RootElement.GetProperty("code").GetString());
            Assert.Null(response.Headers.Location);
        }
    }

    [Theory]
    [InlineData("/api/v1/workspace")]
    [InlineData("/api/v1/workspace/invitation-form")]
    [InlineData("/api/v1/workspace/tenancies")]
    [InlineData("/api/v1/workspace/properties/7")]
    public async Task Workspace_AnonymousAndTenant_CannotReadManagerData(string path)
    {
        await using var factory = Factory(new Administration());
        using var anonymous = Client(factory, "");
        using var denied = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var tenant = Client(factory, "Tenant");
        using var forbidden = await tenant.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Logout_UnsafePost_RequiresAntiforgeryAndExpiresCookie()
    {
        await using var factory = Factory(new Administration());
        using var client = Client(factory, "Tenant");
        using var denied = await client.PostAsJsonAsync("/api/v1/session/logout", new { });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await Csrf(client);
        using var response = await client.PostAsJsonAsync("/api/v1/session/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Identity.Application=;") && value.Contains("expires="));
    }

    private static RentRightApplicationFactory Factory(Administration service) => new("Development", services =>
    {
        services.RemoveAll<IAdministrationService>();
        services.AddSingleton<IAdministrationService>(service);
    });
    private static HttpClient Client(RentRightApplicationFactory factory, string role, bool recent = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        if (role.Length == 0) return client;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(ClaimTypes.Role, role)], IdentityConstants.ApplicationScheme);
        var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) };
        properties.Items[AccountSecurity.PasswordVerified] = AccountSecurity.Timestamp(DateTimeOffset.UtcNow.AddMinutes(recent ? 0 : -11));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }
    private static async Task Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/session/antiforgery");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("requestToken").GetString());
    }
    private sealed class Administration : IAdministrationService
    {
        public int Reads { get; private set; }
        public UpdateUserRolesStatus Status { get; init; } = UpdateUserRolesStatus.Success;
        public UpdateUserRolesCommand? Command { get; private set; }
        public Task<ListAdministrationUsersOutcome> ListUsersAsync(AdministrationUsersQuery query, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(ListAdministrationUsersOutcome.Success(new([], ["Admin", "Landlord", "Tenant"], query.Page, query.PageSize, 0)));
        }
        public Task<UpdateUserRolesOutcome> UpdateUserRolesAsync(UpdateUserRolesCommand command, CancellationToken cancellationToken)
        { Command = command; return Task.FromResult(new UpdateUserRolesOutcome(Status, Errors: ["private dependency details"])); }
    }
}
