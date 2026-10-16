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
using RentRight.Api;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Api;

public class TenanciesApiTests
{
    private const string Route = "/api/v1/tenancies";
    [Theory]
    [InlineData(CreateTenancyStatus.Success, 202)]
    [InlineData(CreateTenancyStatus.PropertyNotFound, 404)]
    [InlineData(CreateTenancyStatus.Conflict, 409)]
    [InlineData(CreateTenancyStatus.ValidationFailed, 400)]
    public async Task Invite_ServiceOutcome_MapsSafeContract(CreateTenancyStatus status, int expected)
    {
        var service = new Service { InviteStatus = status };
        await using var factory = Factory(service);
        using var client = Client(factory, "Landlord");
        await AntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync(Route, new { propertyId = 1, tenantIdentifier = "Tenant@example.test", startDate = "2026-01-01", tenantId = "forged", status = "Active" });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("Tenant@example.test", service.Command!.TenantId);
        if (expected != 202) await ProblemAsync(response, expected);
        else
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Tenant@example.test", body);
            Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(AcceptTenancyStatus.Success, 204)]
    [InlineData(AcceptTenancyStatus.NotFound, 404)]
    [InlineData(AcceptTenancyStatus.Conflict, 409)]
    public async Task Accept_ServiceOutcome_MapsWithoutEchoingToken(AcceptTenancyStatus status, int expected)
    {
        await using var factory = Factory(new Service { AcceptStatus = status });
        using var client = Client(factory, "Tenant");
        await AntiforgeryAsync(client);
        var token = new string('A', 64);
        using var response = await client.PostAsJsonAsync(Route + "/accept", new { token });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        if (expected != 204) await ProblemAsync(response, expected);
    }

    [Theory]
    [InlineData(EndTenancyStatus.Success, 204)]
    [InlineData(EndTenancyStatus.NotFound, 404)]
    [InlineData(EndTenancyStatus.Conflict, 409)]
    public async Task End_ServiceOutcome_MapsProblemDetails(EndTenancyStatus status, int expected)
    {
        await using var factory = Factory(new Service { EndStatus = status });
        using var client = Client(factory, "Admin");
        await AntiforgeryAsync(client);
        using var response = await client.PostAsync(Route + "/1/end", null);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected != 204) await ProblemAsync(response, expected);
    }

    [Fact]
    public async Task Read_ListAndDetail_ExposeOnlyPurposeSpecificFieldsAndBoundPaging()
    {
        await using var factory = Factory(new Service());
        using var client = Client(factory, "Tenant");
        using var list = await client.GetAsync(Route + "?pageSize=1");
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive", body);
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
        using var detail = await client.GetAsync(Route + "/1");
        detail.EnsureSuccessStatusCode();
        using var missing = await client.GetAsync(Route + "/2");
        await ProblemAsync(missing, 404);
        using var invalid = await client.GetAsync(Route + "?pageSize=101");
        await ProblemAsync(invalid, 400);
    }

    [Fact]
    public async Task OpenApi_Lifecycle_DescribesIntentionalTokenFieldsWithoutPersistenceOrIdentityFields()
    {
        await using var factory = Factory(new Service());
        using var client = Client(factory);
        using var response = await client.GetAsync(ApiConventions.OpenApiDocumentPath);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty(Route).GetProperty("post").GetProperty("responses").TryGetProperty("202", out _));
        Assert.True(paths.GetProperty(Route + "/accept").GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.TryGetProperty(Route + "/{id}/end", out _));
        Assert.DoesNotContain("invitationTokenHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedByUserId", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rowVersion", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplicationUser", text);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("AcceptInvitationRequest").GetProperty("properties").TryGetProperty("token", out _));
        Assert.True(schemas.GetProperty("InvitationResponse").GetProperty("properties").TryGetProperty("token", out _));
        Assert.False(schemas.GetProperty("TenancyResponse").GetProperty("properties").TryGetProperty("token", out _));
    }

    private static async Task ProblemAsync(HttpResponseMessage response, int expected)
    {
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("code", out _));
        Assert.True(doc.RootElement.TryGetProperty("traceId", out _));
    }
    private static RentRightApplicationFactory Factory(Service service) => new("Development", services =>
    {
        services.RemoveAll<ITenancyService>();
        services.AddSingleton<ITenancyService>(service);
    });
    private static HttpClient Client(RentRightApplicationFactory factory, params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        if (roles.Length == 0) return client;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "actor") }.Concat(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }
    private static async Task AntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/api-convention-probe/antiforgery");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(ApiConventions.AntiforgeryHeaderName, doc.RootElement.GetProperty("requestToken").GetString());
    }
    private sealed class Service : ITenancyService
    {
        public CreateTenancyStatus InviteStatus = CreateTenancyStatus.Success;
        public AcceptTenancyStatus AcceptStatus = AcceptTenancyStatus.Success;
        public EndTenancyStatus EndStatus = EndTenancyStatus.Success;
        public CreateTenancyCommand? Command;
        private static readonly TenancyAdminRow Row = new(1, "Home", "Address", "sensitive name", "sensitive email", new(2026, 1, 1), null, TenancyStatus.Active);
        public Task<CreateTenancyOutcome> CreateAsync(CreateTenancyCommand command, CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(new CreateTenancyOutcome(InviteStatus, [new("TenantId", "Invalid identifier.")], new(new string('A', 64), DateTime.UtcNow.AddDays(7))));
        }
        public Task<AcceptTenancyOutcome> AcceptAsync(AcceptTenancyCommand command, CancellationToken cancellationToken) => Task.FromResult(new AcceptTenancyOutcome(AcceptStatus));
        public Task<EndTenancyOutcome> EndAsync(int id, CancellationToken cancellationToken) => Task.FromResult(new EndTenancyOutcome(EndStatus));
        public Task<GetTenancyOutcome> GetAsync(int id, CancellationToken cancellationToken) => Task.FromResult(new GetTenancyOutcome(TenancyQueryStatus.Success, id == 1 ? Row : null));
        public Task<ListTenanciesPageOutcome> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new ListTenanciesPageOutcome(TenancyQueryStatus.Success, new([Row], 1)));
        public Task<ListTenanciesOutcome> ListForAdministrationAsync(CancellationToken cancellationToken) => Task.FromResult(ListTenanciesOutcome.Success([Row]));
        public Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(CancellationToken cancellationToken) => Task.FromResult(GetTenancyCreateFormOutcome.Success(new(new(2026, 1, 1), [])));
    }
}
