using RentRight.Application.Abstractions;

namespace RentRight.Authorization;

// Resource facts must come from persisted, server-scoped data, never a bound form.
public sealed record PropertyAccessResource(string LandlordId);
public sealed record TenancyAccessResource(string LandlordId, string? TenantId, bool TenantHasActiveTenancy);
public sealed record MaintenanceAccessResource(string LandlordId, string? TenantId, bool TenantHasActiveTenancy);

public static class RentalAccess
{
    public static bool CanManageProperty(ICurrentUser user, string landlordId) =>
        HasIdentity(user) && (user.IsInRole(AppRoles.Admin) ||
            (user.IsInRole(AppRoles.Landlord) && IsSameUser(user, landlordId)));

    // This is read participation, not permission to accept an invitation for someone else.
    public static bool CanReadTenancy(ICurrentUser user, TenancyAccessResource resource) =>
        CanManageProperty(user, resource.LandlordId) ||
        CanReadOwnActiveTenancy(user, resource.TenantId, resource.TenantHasActiveTenancy);

    public static bool CanReadMaintenance(ICurrentUser user, MaintenanceAccessResource resource) =>
        CanManageProperty(user, resource.LandlordId) ||
        CanReadOwnActiveTenancy(user, resource.TenantId, resource.TenantHasActiveTenancy);

    private static bool CanReadOwnActiveTenancy(ICurrentUser user, string? tenantId, bool active) =>
        HasIdentity(user) && user.IsInRole(AppRoles.Tenant) && active && IsSameUser(user, tenantId);

    private static bool HasIdentity(ICurrentUser user) =>
        user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId);

    private static bool IsSameUser(ICurrentUser user, string? resourceUserId) =>
        !string.IsNullOrWhiteSpace(resourceUserId) &&
        string.Equals(user.UserId, resourceUserId, StringComparison.Ordinal);
}
