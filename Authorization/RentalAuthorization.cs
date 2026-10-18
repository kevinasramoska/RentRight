using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RentRight.Application.Abstractions;

namespace RentRight.Authorization;

public static class RentalPolicies
{
    // Endpoint audience policies are deliberately separate from resource policies.
    public const string Manager = "Rental.Manager";
    public const string Landlord = "Rental.Landlord";
    public const string Tenant = "Rental.Tenant";
    public const string Participant = "Rental.Participant";
    public const string AdministerRoles = "Rental.AdministerRoles";
    public const string ManageProperty = "Rental.ManageProperty";
    public const string ParticipateInTenancy = "Rental.ParticipateInTenancy";
    public const string ReadMaintenance = "Rental.ReadMaintenance";
    public const string TransitionMaintenance = "Rental.TransitionMaintenance";
}

public enum RentalOperation { ManageProperty, ReadTenancy, ReadMaintenance, TransitionMaintenance }
public sealed record RentalResourceRequirement(RentalOperation Operation) : IAuthorizationRequirement;

public static class RentalAuthorization
{
    public static IServiceCollection AddRentalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            AddAudience(RentalPolicies.Manager, AppRoles.Admin, AppRoles.Landlord);
            AddAudience(RentalPolicies.Landlord, AppRoles.Landlord);
            AddAudience(RentalPolicies.Tenant, AppRoles.Tenant);
            AddAudience(RentalPolicies.Participant, AppRoles.Admin, AppRoles.Landlord, AppRoles.Tenant);
            AddAudience(RentalPolicies.AdministerRoles, AppRoles.Admin);
            AddResourcePolicy(RentalPolicies.ManageProperty, RentalOperation.ManageProperty);
            AddResourcePolicy(RentalPolicies.ParticipateInTenancy, RentalOperation.ReadTenancy);
            AddResourcePolicy(RentalPolicies.ReadMaintenance, RentalOperation.ReadMaintenance);
            AddResourcePolicy(RentalPolicies.TransitionMaintenance, RentalOperation.TransitionMaintenance);

            void AddAudience(string name, params string[] roles) => options.AddPolicy(name, policy => policy
                .RequireAuthenticatedUser().RequireRole(roles)
                .RequireAssertion(context => !string.IsNullOrWhiteSpace(context.User.FindFirstValue(ClaimTypes.NameIdentifier))));

            void AddResourcePolicy(string name, RentalOperation operation) =>
                options.AddPolicy(name, policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new RentalResourceRequirement(operation)));
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, RentalResourceHandler>());
        return services;
    }
}

public sealed class RentalResourceHandler : AuthorizationHandler<RentalResourceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RentalResourceRequirement requirement)
    {
        var user = new PrincipalUser(context.User);
        var allowed = (requirement.Operation, context.Resource) switch
        {
            (RentalOperation.ManageProperty, PropertyAccessResource resource) => RentalAccess.CanManageProperty(user, resource.LandlordId),
            (RentalOperation.ReadTenancy, TenancyAccessResource resource) => RentalAccess.CanReadTenancy(user, resource),
            (RentalOperation.ReadMaintenance, MaintenanceAccessResource resource) => RentalAccess.CanReadMaintenance(user, resource),
            (RentalOperation.TransitionMaintenance, MaintenanceAccessResource resource) => RentalAccess.CanManageProperty(user, resource.LandlordId),
            _ => false
        };
        if (allowed) context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private sealed class PrincipalUser(ClaimsPrincipal principal) : ICurrentUser
    {
        public bool IsAuthenticated => principal.Identity?.IsAuthenticated == true;
        public string? UserId => principal.FindFirstValue(ClaimTypes.NameIdentifier);
        public bool IsInRole(string roleName) => principal.IsInRole(roleName);
    }
}
