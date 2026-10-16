using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Features.Dashboards.Contracts;
using RentRight.Models;

namespace RentRight.Features.Dashboards;

public sealed class EfDashboardDataStore(ApplicationDbContext dbContext) : IDashboardDataStore
{
    public async Task<AdminDashboardSummary> GetAdminSummaryAsync(
        CancellationToken cancellationToken)
    {
        var totalUsers = await dbContext.Users
            .AsNoTracking()
            .CountAsync(cancellationToken);
        var totalProperties = await dbContext.Properties
            .AsNoTracking()
            .CountAsync(cancellationToken);
        return new AdminDashboardSummary(totalUsers, totalProperties);
    }

    public async Task<IReadOnlyList<LandlordPropertySummary>> ListLandlordPropertiesAsync(
        string landlordId,
        int maximumResults,
        CancellationToken cancellationToken) =>
        await dbContext.Properties
            .AsNoTracking()
            .Where(property => property.LandlordId == landlordId)
            .OrderBy(property => property.Title)
            .ThenBy(property => property.Id)
            .Take(maximumResults)
            .Select(property => new LandlordPropertySummary(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantTenancySummary>> ListTenantTenanciesAsync(
        string tenantId,
        DateOnly today,
        int maximumResults,
        CancellationToken cancellationToken) =>
        await dbContext.Tenancies
            .AsNoTracking()
            .Where(tenancy => tenancy.TenantId == tenantId &&
                (tenancy.InvitedUtc == null || tenancy.AcceptedUtc != null))
            .OrderByDescending(tenancy => tenancy.Status == TenancyStatus.Active)
            .ThenByDescending(tenancy => tenancy.StartDate)
            .ThenByDescending(tenancy => tenancy.Id)
            .Take(maximumResults)
            .Select(tenancy => new TenantTenancySummary(
                tenancy.Id,
                tenancy.Property!.Title,
                tenancy.Property.Address,
                tenancy.StartDate,
                tenancy.EndDate,
                tenancy.Status,
                tenancy.Status == TenancyStatus.Active &&
                    tenancy.StartDate <= today &&
                    (tenancy.EndDate == null || tenancy.EndDate >= today)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMaintenanceSummary>> ListTenantMaintenanceAsync(
        string tenantId,
        int maximumResults,
        CancellationToken cancellationToken) =>
        await dbContext.MaintenanceRequests
            .AsNoTracking()
            .Where(request => request.Tenancy!.TenantId == tenantId)
            .OrderByDescending(request => request.DateSubmitted)
            .ThenByDescending(request => request.Id)
            .Take(maximumResults)
            .Select(request => new TenantMaintenanceSummary(
                request.Id,
                request.Tenancy!.Property!.Title,
                request.DateSubmitted,
                request.Status))
            .ToListAsync(cancellationToken);
}
