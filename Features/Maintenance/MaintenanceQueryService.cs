using RentRight.Application.Abstractions;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class MaintenanceQueryService(
    IMaintenanceStore store,
    ICurrentUser currentUser,
    IClock clock) : IMaintenanceQueryService
{
    private const string AdministratorRole = "Admin";
    private const string LandlordRole = "Landlord";
    private const string TenantRole = "Tenant";

    public async Task<MaintenanceListOutcome> ListAsync(
        MaintenanceRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return MaintenanceListOutcome.Unauthenticated();
        }

        var isAdministrator = currentUser.IsInRole(AdministratorRole);
        var isLandlord = currentUser.IsInRole(LandlordRole);
        var isTenant = currentUser.IsInRole(TenantRole);
        if (!isAdministrator && !isLandlord && !isTenant)
        {
            return MaintenanceListOutcome.Forbidden();
        }

        var page = Math.Clamp(query.Page, 1, MaintenanceRequestsQuery.MaximumPage);
        var pageSize = Math.Clamp(
            query.PageSize,
            1,
            MaintenanceRequestsQuery.MaximumPageSize);
        var storeRows = await store.ListAsync(
            new MaintenanceListAccess(
                userId,
                IncludeAll: isAdministrator,
                IncludeLandlordOwned: !isAdministrator && isLandlord,
                IncludeTenantOwned: !isAdministrator && isTenant,
                Today: DateOnly.FromDateTime(clock.UtcNow)),
            page,
            pageSize,
            cancellationToken);

        var rows = storeRows
            .Select(row => new MaintenanceRequestRow(
                row.Id,
                row.PropertyTitle,
                row.TenantName,
                row.Description,
                row.DateSubmitted,
                row.Status,
                CanUpdateStatus: isAdministrator ||
                    (isLandlord && row.LandlordId == userId)))
            .ToArray();
        var audience = isAdministrator
            ? MaintenanceListAudience.Administrator
            : isLandlord
                ? MaintenanceListAudience.Landlord
                : MaintenanceListAudience.Tenant;

        return MaintenanceListOutcome.Success(new MaintenanceRequestList(
            audience,
            CanCreate: isTenant,
            rows,
            page,
            pageSize));
    }

    public async Task<MaintenanceDetailOutcome> GetDetailAsync(
        int requestId,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return MaintenanceDetailOutcome.Unauthenticated();
        }

        var isAdministrator = currentUser.IsInRole(AdministratorRole);
        var isLandlord = currentUser.IsInRole(LandlordRole);
        var isTenant = currentUser.IsInRole(TenantRole);
        if (!isAdministrator && !isLandlord && !isTenant)
        {
            return MaintenanceDetailOutcome.Forbidden();
        }

        var request = await store.GetRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return MaintenanceDetailOutcome.NotFound();
        }

        var landlordOwnsRequest = isLandlord && request.LandlordId == userId;
        var tenantOwnsRequest = isTenant && request.TenantId == userId;
        if (tenantOwnsRequest)
        {
            var activeTenancies = await store.ListActiveTenanciesAsync(userId, DateOnly.FromDateTime(clock.UtcNow), cancellationToken);
            tenantOwnsRequest = activeTenancies.Any(t => t.Id == request.TenancyId);
        }
        if (!isAdministrator && !landlordOwnsRequest && !tenantOwnsRequest)
        {
            return MaintenanceDetailOutcome.ConcealedNotFound();
        }

        return MaintenanceDetailOutcome.Success(new MaintenanceRequestDetail(
            request.Id,
            request.PropertyTitle,
            request.PropertyAddress,
            request.TenantName,
            request.Description,
            request.DateSubmitted,
            request.LastUpdatedUtc,
            request.Status,
            CanUpdateStatus: isAdministrator || landlordOwnsRequest)
        {
            History = await store.GetHistoryAsync(requestId, cancellationToken)
        });
    }

    public async Task<MaintenanceCreateFormOutcome> GetCreateFormAsync(
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return MaintenanceCreateFormOutcome.Unauthenticated();
        }

        if (!currentUser.IsInRole(TenantRole))
        {
            return MaintenanceCreateFormOutcome.Forbidden();
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var tenancies = await store.ListActiveTenanciesAsync(
            userId,
            today,
            cancellationToken);
        var options = tenancies
            .Select(tenancy => new MaintenanceTenancyOption(
                tenancy.Id,
                tenancy.PropertyTitle,
                tenancy.PropertyAddress))
            .ToArray();

        return MaintenanceCreateFormOutcome.Success(new MaintenanceCreateForm(options));
    }

    public async Task<MaintenanceStatusUpdateOutcome> GetStatusUpdateAsync(
        int requestId,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return MaintenanceStatusUpdateOutcome.Unauthenticated();
        }

        var isAdministrator = currentUser.IsInRole(AdministratorRole);
        var isLandlord = currentUser.IsInRole(LandlordRole);
        if (!isAdministrator && !isLandlord)
        {
            return MaintenanceStatusUpdateOutcome.Forbidden();
        }

        var request = await store.GetRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return MaintenanceStatusUpdateOutcome.NotFound();
        }

        if (!isAdministrator && request.LandlordId != userId)
        {
            return MaintenanceStatusUpdateOutcome.ConcealedNotFound();
        }

        return MaintenanceStatusUpdateOutcome.Success(new MaintenanceStatusUpdate(
            request.Id,
            request.PropertyTitle,
            request.TenantName,
            request.Status,
            request.Status.GetAllowedTransitions()));
    }

    private string? GetAuthenticatedUserId() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : null;
}
