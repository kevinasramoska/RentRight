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
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.Api;

public class ApiConventionsTests
{
    private const string ProbeRoute = "/api/v1/api-convention-probe";

    [Fact]
    public async Task GetSample_Success_UsesCamelCaseJsonEnvelopeAndUtcTimestamp()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ProbeRoute + "/sample");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"totalPages\"", body);
        Assert.DoesNotContain("\"TotalPages\"", body);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(7, item.GetProperty("itemId").GetInt32());
        Assert.Equal("2026-08-11T12:30:00Z", item.GetProperty("updatedUtc").GetString());
        Assert.True(item.GetProperty("requestAbortedObserved").GetBoolean());
    }

    [Fact]
    public async Task PostValidation_InvalidInput_ReturnsValidationProblemDetails()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            ProbeRoute + "/validate",
            new { });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(
            "displayName",
            out _));
    }

    [Fact]
    public async Task GetSample_ExcessivePageSize_ReturnsValidationProblemDetails()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            ProbeRoute + $"/sample?pageSize={ApiConventions.MaximumPageSize + 1}");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(
            "pageSize",
            out _));
    }

    [Theory]
    [InlineData(PropertyOperationStatus.Unauthenticated, HttpStatusCode.Unauthorized,
        ApiErrorCodes.AuthenticationRequired)]
    [InlineData(PropertyOperationStatus.Forbidden, HttpStatusCode.Forbidden,
        ApiErrorCodes.AccessDenied)]
    [InlineData(PropertyOperationStatus.NotFound, HttpStatusCode.NotFound,
        ApiErrorCodes.ResourceNotFound)]
    [InlineData(PropertyOperationStatus.Conflict, HttpStatusCode.Conflict,
        ApiErrorCodes.ResourceConflict)]
    public async Task GetOutcome_ApplicationFailure_ReturnsStableProblemContract(
        PropertyOperationStatus status,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ProbeRoute + $"/outcomes/{status}");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("SQL", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetException_UnexpectedFailure_ReturnsSafeInternalProblemDetails()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ProbeRoute + "/exception");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiErrorCodes.InternalError,
            problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("credential detail", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL provider", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal frame", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOpenApi_Development_GeneratesVersionedRunningContract()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ApiConventions.OpenApiDocumentPath);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RentRight API",
            document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1",
            document.RootElement.GetProperty("info").GetProperty("version").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty(
            ProbeRoute + "/sample",
            out _));
        var schemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        Assert.True(schemes.TryGetProperty(
            ApiConventions.IdentityCookieSecurityScheme,
            out var cookieScheme));
        Assert.Equal("cookie", cookieScheme.GetProperty("in").GetString());
        var secureOperation = document.RootElement
            .GetProperty("paths")
            .GetProperty(ProbeRoute + "/secure")
            .GetProperty("get");
        Assert.Equal(
            JsonValueKind.Array,
            secureOperation.GetProperty("security")[0]
                .GetProperty(ApiConventions.IdentityCookieSecurityScheme)
                .ValueKind);
    }

    [Fact]
    public async Task GetOpenApi_Production_IsNotExposed()
    {
        await using var factory = CreateFactory("Production");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ApiConventions.OpenApiDocumentPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecure_AnonymousCookieChallenge_ReturnsProblemDetailsWithoutRedirect()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ProbeRoute + "/secure");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            ApiErrorCodes.AuthenticationRequired,
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task PostSecure_AuthenticatedCookieWithoutAntiforgeryHeader_IsRejected()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateClient(factory);
        AddIdentityCookie(client, factory);

        using var response = await client.PostAsJsonAsync(
            ProbeRoute + "/secure",
            new { });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            ApiErrorCodes.InvalidRequest,
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
    }

    private static RentRightApplicationFactory CreateFactory(string environment) =>
        new(environment);

    private static HttpClient CreateClient(RentRightApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static void AddIdentityCookie(
        HttpClient client,
        RentRightApplicationFactory factory)
    {
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "api-test-user")],
            IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            IdentityConstants.ApplicationScheme);
        var cookieValue = cookieOptions.TicketDataFormat.Protect(ticket);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={cookieValue}");
    }
}
