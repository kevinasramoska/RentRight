using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using RentRight.Authorization;
using Xunit;

namespace RentRight.Tests.Authorization;

public sealed class RentalAuthorizationTests
{
    [Theory]
    [InlineData(false, "owner", "Admin", false, false, false)]
    [InlineData(true, "owner", "", false, false, false)]
    [InlineData(true, "tenant", "Tenant", false, true, false)]
    [InlineData(true, "other", "Tenant", false, false, false)]
    [InlineData(true, "owner", "Landlord", true, true, false)]
    [InlineData(true, "other", "Landlord", false, false, false)]
    [InlineData(true, "other", "Admin", true, true, true)]
    [InlineData(true, "tenant", "Admin,Landlord,Tenant", true, true, true)]
    [InlineData(true, "tenant", "Landlord,Tenant", false, true, false)]
    [InlineData(true, "owner", "Landlord,Tenant", true, true, false)]
    [InlineData(true, null, "Admin", false, false, false)]
    [InlineData(true, " ", "Landlord", false, false, false)]
    [InlineData(true, "OWNER", "Landlord", false, false, false)]
    [InlineData(true, "owner", "landlord", false, false, false)]
    public async Task Authorize_RoleAndOwnershipMatrix_EnforcesResourceBoundaries(
        bool authenticated, string? userId, string roles, bool manage, bool read, bool administerRoles)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = User(authenticated, userId, roles);

        Assert.Equal(manage, (await authorization.AuthorizeAsync(user, new PropertyAccessResource("owner"), RentalPolicies.ManageProperty)).Succeeded);
        Assert.Equal(read, (await authorization.AuthorizeAsync(user, new TenancyAccessResource("owner", "tenant", true), RentalPolicies.ParticipateInTenancy)).Succeeded);
        Assert.Equal(read, (await authorization.AuthorizeAsync(user, new MaintenanceAccessResource("owner", "tenant", true), RentalPolicies.ReadMaintenance)).Succeeded);
        Assert.Equal(manage, (await authorization.AuthorizeAsync(user, new MaintenanceAccessResource("owner", "tenant", true), RentalPolicies.TransitionMaintenance)).Succeeded);
        Assert.Equal(administerRoles, (await authorization.AuthorizeAsync(user, null, RentalPolicies.AdministerRoles)).Succeeded);
    }

    [Theory]
    [InlineData(RentalPolicies.ParticipateInTenancy)]
    [InlineData(RentalPolicies.ReadMaintenance)]
    public async Task Authorize_TenantWithoutCurrentTenancy_DeniesHistory(string policy)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        object resource = policy == RentalPolicies.ParticipateInTenancy
            ? new TenancyAccessResource("owner", "tenant", false)
            : new MaintenanceAccessResource("owner", "tenant", false);

        var result = await authorization.AuthorizeAsync(User(true, "tenant", "Tenant"), resource, policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(RentalPolicies.ManageProperty)]
    [InlineData(RentalPolicies.ParticipateInTenancy)]
    [InlineData(RentalPolicies.ReadMaintenance)]
    [InlineData(RentalPolicies.TransitionMaintenance)]
    public async Task Authorize_MissingOrWrongResource_DeniesEvenAdmin(string policy)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = User(true, "admin", "Admin");

        Assert.False((await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(user, new object(), policy)).Succeeded);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Landlord")]
    public async Task Authorize_ManagerWithEndedTenancy_RetainsHistoryAccess(string role)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = User(true, "owner", role);

        Assert.True((await authorization.AuthorizeAsync(user,
            new TenancyAccessResource("owner", "tenant", false), RentalPolicies.ParticipateInTenancy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(user,
            new MaintenanceAccessResource("owner", "tenant", false), RentalPolicies.ReadMaintenance)).Succeeded);
    }

    [Theory]
    [InlineData(false, "Admin", false, false, false)]
    [InlineData(true, "", false, false, false)]
    [InlineData(true, "Tenant", false, true, true)]
    [InlineData(true, "Landlord", true, false, true)]
    [InlineData(true, "Admin", true, false, true)]
    [InlineData(true, "Admin,Landlord,Tenant", true, true, true)]
    public async Task Authorize_EndpointAudiences_PreserveRoleCapabilities(
        bool authenticated, string roles, bool manager, bool tenant, bool participant)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = User(authenticated, "user", roles);

        Assert.Equal(manager, (await authorization.AuthorizeAsync(user, null, RentalPolicies.Manager)).Succeeded);
        Assert.Equal(tenant, (await authorization.AuthorizeAsync(user, null, RentalPolicies.Tenant)).Succeeded);
        Assert.Equal(participant, (await authorization.AuthorizeAsync(user, null, RentalPolicies.Participant)).Succeeded);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddRentalAuthorization().BuildServiceProvider();

    [Theory]
    [InlineData(RentalPolicies.Manager)]
    [InlineData(RentalPolicies.Landlord)]
    [InlineData(RentalPolicies.Tenant)]
    [InlineData(RentalPolicies.Participant)]
    [InlineData(RentalPolicies.AdministerRoles)]
    public async Task Authorize_AudienceWithoutUserIdentifier_FailsClosed(string policy)
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        foreach (var id in new string?[] { null, "", " " })
            Assert.False((await authorization.AuthorizeAsync(User(true, id, "Admin,Landlord,Tenant"), null, policy)).Succeeded);
    }

    [Theory]
    [InlineData("Admin", false)]
    [InlineData("Landlord", true)]
    [InlineData("Tenant", false)]
    [InlineData("Admin,Landlord,Tenant", true)]
    public async Task Authorize_LandlordDashboard_PreservesDedicatedRoleRequirement(string roles, bool expected)
    {
        using var services = Services();
        Assert.Equal(expected, (await services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User(true, "user", roles), null, RentalPolicies.Landlord)).Succeeded);
    }

    private static ClaimsPrincipal User(bool authenticated, string? userId, string roles)
    {
        var identity = new ClaimsIdentity(authenticated ? "Test" : null);
        if (userId is not null) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries))
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }
}
