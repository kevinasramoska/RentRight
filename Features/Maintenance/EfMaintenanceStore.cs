using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class EfMaintenanceStore(ApplicationDbContext dbContext) : IMaintenanceStore
{
    public async Task<IReadOnlyList<MaintenanceListStoreRow>> ListAsync(
        MaintenanceListAccess access,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var requests = dbContext.MaintenanceRequests.AsNoTracking();

        if (!access.IncludeAll)
        {
            requests = requests.Where(request =>
                (access.IncludeLandlordOwned &&
                    request.Tenancy!.Property!.LandlordId == access.UserId) ||
                (access.IncludeTenantOwned &&
                    request.Tenancy!.TenantId == access.UserId));
        }

        return await requests
            .OrderByDescending(request => request.DateSubmitted)
            .ThenByDescending(request => request.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(request => new MaintenanceListStoreRow(
                request.Id,
                request.Tenancy!.Property!.Title,
                request.Tenancy.Tenant!.FullName,
                request.Description,
                request.DateSubmitted,
                request.Status,
                request.Tenancy.Property.LandlordId,
                request.Tenancy.TenantId))
            .ToListAsync(cancellationToken);
    }

    public Task<MaintenanceRequestStoreData?> GetRequestAsync(
        int requestId,
        CancellationToken cancellationToken) =>
        dbContext.MaintenanceRequests
            .AsNoTracking()
            .Where(request => request.Id == requestId)
            .Select(request => new MaintenanceRequestStoreData(
                request.Id,
                request.Tenancy!.Property!.Title,
                request.Tenancy.Property.Address,
                request.Tenancy.Tenant!.FullName,
                request.Description,
                request.DateSubmitted,
                request.LastUpdatedUtc,
                request.Status,
                request.Tenancy.TenantId,
                request.Tenancy.Property.LandlordId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MaintenanceTenancyStoreData>> ListActiveTenanciesAsync(
        string tenantId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        await dbContext.Tenancies
            .AsNoTracking()
            .Where(tenancy =>
                tenancy.TenantId == tenantId &&
                tenancy.Status == TenancyStatus.Active &&
                tenancy.StartDate <= date &&
                (tenancy.EndDate == null || tenancy.EndDate >= date))
            .OrderBy(tenancy => tenancy.Property!.Title)
            .ThenBy(tenancy => tenancy.Property!.Address)
            .ThenBy(tenancy => tenancy.Id)
            .Select(tenancy => new MaintenanceTenancyStoreData(
                tenancy.Id,
                tenancy.TenantId,
                tenancy.Status,
                tenancy.StartDate,
                tenancy.EndDate,
                tenancy.Property!.Title,
                tenancy.Property.Address))
            .ToListAsync(cancellationToken);

    public Task<MaintenanceTenancyStoreData?> GetTenancyAsync(
        int tenancyId,
        CancellationToken cancellationToken) =>
        dbContext.Tenancies
            .AsNoTracking()
            .Where(tenancy => tenancy.Id == tenancyId)
            .Select(tenancy => new MaintenanceTenancyStoreData(
                tenancy.Id,
                tenancy.TenantId,
                tenancy.Status,
                tenancy.StartDate,
                tenancy.EndDate,
                tenancy.Property!.Title,
                tenancy.Property.Address))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> CreateAsync(
        NewMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new MaintenanceRequest
        {
            TenancyId = request.TenancyId,
            Description = request.Description,
            Status = request.Status,
            DateSubmitted = request.DateSubmitted,
            LastUpdatedUtc = request.LastUpdatedUtc
        };

        dbContext.MaintenanceRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<bool> TryUpdateStatusAsync(
        int requestId,
        MaintenanceRequestStatus expectedStatus,
        MaintenanceRequestStatus newStatus,
        DateTime lastUpdatedUtc,
        CancellationToken cancellationToken)
    {
        var affectedRows = await dbContext.MaintenanceRequests
            .Where(request => request.Id == requestId && request.Status == expectedStatus)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(request => request.Status, newStatus)
                    .SetProperty(request => request.LastUpdatedUtc, lastUpdatedUtc),
                cancellationToken);

        return affectedRows == 1;
    }
}
