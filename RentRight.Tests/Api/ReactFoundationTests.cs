using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RentRight.Api;
using Xunit;

namespace RentRight.Tests.Api;

public class ReactFoundationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_AnonymousOrCookie_ExposesOnlyPresentationContract(bool authenticated)
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        if (authenticated) AddCookie(client, factory);

        using var response = await client.GetAsync("/api/v1/session");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(["isAuthenticated", "roles"], document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(authenticated, document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(authenticated ? ["Tenant"] : Array.Empty<string>(),
            document.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Antiforgery_AnonymousOrCookie_NoStoreAndAcceptedByGlobalFilter(bool authenticated)
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        if (authenticated) AddCookie(client, factory);

        using var response = await client.GetAsync("/api/v1/session/antiforgery");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Single(document.RootElement.EnumerateObject());
        var token = document.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        if (authenticated)
        {
            const string route = "/api/v1/api-convention-probe/secure";
            using var rejected = await client.PostAsJsonAsync(route, new { });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName, token);
            using var accepted = await client.PostAsJsonAsync(route, new { });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task ReactHost_BuiltAssets_OnlyAppRoutesFallBackToHost()
    {
        // Run npm run build before HTTP tests; generated assets are intentionally untracked.
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/app/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"root\"", html);
        var assets = Regex.Matches(html, "(?:src|href)=\"(/app/assets/[^\"]+)\"");
        Assert.True(assets.Count >= 2, "Expected hashed JavaScript and CSS assets. Run npm run build in ClientApp first.");
        foreach (Match asset in assets)
        {
            using var assetResponse = await client.GetAsync(asset.Groups[1].Value);
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
            Assert.DoesNotContain("text/html", assetResponse.Content.Headers.ContentType?.MediaType ?? "");
        }
        using var root = await client.GetAsync("/app");
        Assert.Equal(html, await root.Content.ReadAsStringAsync());
        using var nested = await client.GetAsync("/app/future-route");
        Assert.Equal(html, await nested.Content.ReadAsStringAsync());
        using var missingAsset = await client.GetAsync("/app/assets/missing.js");
        Assert.Equal(HttpStatusCode.NotFound, missingAsset.StatusCode);
    }

    [Theory]
    [InlineData("/Home/Privacy", HttpStatusCode.OK)]
    [InlineData("/Identity/Account/Login", HttpStatusCode.OK)]
    [InlineData("/Dashboard", HttpStatusCode.Redirect)]
    [InlineData("/api/v1/session", HttpStatusCode.OK)]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("/openapi/v1.json", HttpStatusCode.NotFound)]
    [InlineData("/Home/Error", HttpStatusCode.OK)]
    [InlineData("/css/site.css", HttpStatusCode.OK)]
    public async Task ExistingRoutes_OutsideApp_AreNotSwallowed(string path, HttpStatusCode expected)
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", await response.Content.ReadAsStringAsync());
    }

    private static HttpClient CreateClient(RentRightApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static void AddCookie(HttpClient client, RentRightApplicationFactory factory)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "test-internal-id"),
            new Claim(ClaimTypes.Name, "private-account-name"),
            new Claim(ClaimTypes.Role, "Tenant"), new Claim(ClaimTypes.Role, "Unrecognized")
        ], IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }
}
