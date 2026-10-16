using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class EfMaintenanceStore(ApplicationDbContext dbContext) : IMaintenanceStore
{
    public async Task<IReadOnlyList<MaintenanceHistoryItem>> GetHistoryAsync(int requestId, CancellationToken cancellationToken) =>
        await dbContext.MaintenanceStatusEvents.AsNoTracking().Where(e => e.MaintenanceRequestId == requestId)
            .OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id)
            .Select(e => new MaintenanceHistoryItem(e.Status.ToString(), e.OccurredUtc, e.IsLegacyBaseline))
            .ToListAsync(cancellationToken);

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
                (access.IncludeTenantOwned && request.Tenancy!.TenantId == access.UserId &&
                    request.Tenancy.Status == TenancyStatus.Active && request.Tenancy.StartDate <= access.Today &&
                    (request.Tenancy.EndDate == null || request.Tenancy.EndDate >= access.Today) &&
                    (request.Tenancy.InvitedUtc == null || request.Tenancy.AcceptedUtc != null)));
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
                request.Tenancy.Property.LandlordId,
                request.TenancyId))
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
                (tenancy.InvitedUtc == null || tenancy.AcceptedUtc != null) &&
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
        // Use the same property lock as tenancy end/accept, then revalidate inside
        // the transaction. A stale form or racing end cannot create a request.
        var propertyId = await dbContext.Tenancies.AsNoTracking().Where(t => t.Id == request.TenancyId)
            .Select(t => (int?)t.PropertyId).SingleOrDefaultAsync(cancellationToken);
        if (propertyId is null) throw new DbUpdateConcurrencyException();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Properties.FromSqlInterpolated($"SELECT * FROM [Properties] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {propertyId.Value}")
            .AsNoTracking().SingleAsync(cancellationToken);
        var today = DateOnly.FromDateTime(request.DateSubmitted);
        if (!await dbContext.Tenancies.AnyAsync(t => t.Id == request.TenancyId && t.TenantId == request.TenantId
            && t.Status == TenancyStatus.Active && t.StartDate <= today && (t.EndDate == null || t.EndDate >= today)
            && (t.InvitedUtc == null || t.AcceptedUtc != null), cancellationToken))
            throw new DbUpdateConcurrencyException();
        var entity = new MaintenanceRequest
        {
            TenancyId = request.TenancyId,
            Description = request.Description,
            Status = request.Status,
            DateSubmitted = request.DateSubmitted,
            LastUpdatedUtc = request.LastUpdatedUtc,
            StatusHistory = [new MaintenanceStatusEvent(MaintenanceRequestStatus.Submitted, request.DateSubmitted)]
        };

        dbContext.MaintenanceRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<bool> TryUpdateStatusAsync(
        int requestId,
        MaintenanceRequestStatus expectedStatus,
        MaintenanceRequestStatus newStatus,
        DateTime lastUpdatedUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var affectedRows = await dbContext.MaintenanceRequests
            .Where(request => request.Id == requestId && request.Status == expectedStatus)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(request => request.Status, newStatus)
                    .SetProperty(request => request.LastUpdatedUtc, lastUpdatedUtc),
                cancellationToken);

        if (affectedRows != 1) return false;
        // Retain the existing Razor command, recording its already-supported transitions.
        dbContext.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent(newStatus, lastUpdatedUtc, maintenanceRequestId: requestId));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
