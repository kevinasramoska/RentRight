using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RentRight.Api;
using RentRight.Security;
using Xunit;

namespace RentRight.Tests.Api;

public sealed class BrowserSecurityTests
{
    private const string Probe = "/api/v1/api-convention-probe";

    [Theory]
    [InlineData("/Identity/Account/Login", 200)]
    [InlineData("/api/v1/session", 200)]
    [InlineData("/css/site.css", 200)]
    [InlineData("/app/", 200)]
    [InlineData("/Dashboard", 302)]
    [InlineData("/api/v1/missing", 404)]
    [InlineData(Probe + "/exception", 500)]
    public async Task Headers_StaticMvcIdentityApiAndErrors_ApplyBaselineWithoutBreakingRoutes(string path, int status)
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(path);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(BrowserSecurity.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task AuthenticationLimit_PostsShareIpBudgetAndIgnoreSpoofedForwardingHeaders_ReadsRemainAvailable()
    {
        await using var factory = LimitedFactory();
        using var client = CreateClient(factory);
        using var first = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent([]));
        using var second = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");

        using var limited = await client.PostAsync("/identity/account/login?returnUrl=/different", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("text/plain", limited.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Please wait", await limited.Content.ReadAsStringAsync());
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(limited.Headers.CacheControl?.NoStore);
        Assert.Contains("nosniff", limited.Headers.GetValues("X-Content-Type-Options"));
        using var login = await client.GetAsync("/Identity/Account/Login");
        using var token = await client.GetAsync("/api/v1/session/antiforgery");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
    }

    [Fact]
    public async Task MutationLimit_PerPersistedIdentityBudget_ReturnsProblemDetailsWithoutAutomaticRetry()
    {
        await using var factory = LimitedFactory();
        using var firstUser = CreateClient(factory);
        AddCookie(firstUser, factory, "first-user");
        for (var index = 0; index < 2; index++)
        {
            using var accepted = await firstUser.PostAsJsonAsync(Probe + "/validate", new { displayName = "Valid" });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var limited = await firstUser.PostAsJsonAsync(Probe + "/validate?different=route", new { displayName = "private-value" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        var body = await limited.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(ApiErrorCodes.RateLimited, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(limited.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("private-value", body);

        using var secondUser = CreateClient(factory);
        AddCookie(secondUser, factory, "second-user");
        using var independent = await secondUser.PostAsJsonAsync(Probe + "/validate", new { displayName = "Valid" });
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        using var read = await firstUser.GetAsync("/api/v1/session");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutationLimit_AllUnsafeMethods_AnonymousRequestsShareBudgetAcrossPaths(string method)
    {
        await using var factory = LimitedFactory();
        using var client = CreateClient(factory);
        for (var index = 0; index < 2; index++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Probe + "/secure");
            using var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using var limitedRequest = new HttpRequestMessage(new HttpMethod(method), "/api/v1/different-path");
        using var limited = await client.SendAsync(limitedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Antiforgery_AnonymousOrDifferentUserToken_CannotAuthorizeCurrentUserWrite(bool anonymousToken)
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        if (!anonymousToken) AddCookie(client, factory, "previous-user");
        using var bootstrap = await client.GetAsync("/api/v1/session/antiforgery");
        using var document = JsonDocument.Parse(await bootstrap.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("Cookie");
        AddCookie(client, factory, "current-user");
        client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName,
            document.RootElement.GetProperty("requestToken").GetString());
        using var rejected = await client.PostAsJsonAsync(Probe + "/secure", new { });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        client.DefaultRequestHeaders.Remove(ApiConventions.AntiforgeryHeaderName);
        using var fresh = await client.GetAsync("/api/v1/session/antiforgery");
        using var current = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName,
            current.RootElement.GetProperty("requestToken").GetString());
        using var accepted = await client.PostAsJsonAsync(Probe + "/secure", new { });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task BrowserBoundary_ForeignOriginHasNoCorsPermissionAndCannotWriteWithoutToken()
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        AddCookie(client, factory, "current-user");
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var preflightRequest = new HttpRequestMessage(HttpMethod.Options, Probe + "/secure");
        preflightRequest.Headers.Add("Access-Control-Request-Method", "POST");
        preflightRequest.Headers.Add("Access-Control-Request-Headers", ApiConventions.AntiforgeryHeaderName);
        using var preflight = await client.SendAsync(preflightRequest);
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Credentials"));
        using var write = await client.PostAsJsonAsync(Probe + "/secure", new { });
        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.False(write.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AntiforgeryCookie_HttpsBootstrap_IsSecureHttpOnlyStrictAndNoStore()
    {
        await using var factory = new RentRightApplicationFactory("Production");
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/session/antiforgery");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Antiforgery."));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RateLimit_InvalidConfiguration_FailsStartupInsteadOfDisablingProtection()
    {
        await using var factory = new RentRightApplicationFactory("Production", services =>
            services.Configure<BrowserRateLimitOptions>(options => options.MutationPermitLimit = 0));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private static RentRightApplicationFactory LimitedFactory() => new("Production", services =>
        services.Configure<BrowserRateLimitOptions>(options =>
        {
            options.AuthenticationPermitLimit = 2;
            options.MutationPermitLimit = 2;
            options.WindowSeconds = 3600;
        }));

    private static HttpClient CreateClient(RentRightApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static void AddCookie(HttpClient client, RentRightApplicationFactory factory, string userId)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], IdentityConstants.ApplicationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }
}
