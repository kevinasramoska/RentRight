using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Authorization;
using RentRight.Data;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class TenantMaintenanceService(ApplicationDbContext db, ICurrentUser user, IClock clock,
    IMaintenanceCommandService commands) : ITenantMaintenanceService
{
    private TenantMaintenanceStatus Access => !user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
        ? TenantMaintenanceStatus.Unauthenticated : !user.IsInRole(AppRoles.Tenant)
            ? TenantMaintenanceStatus.Forbidden : TenantMaintenanceStatus.Success;

    private IQueryable<MaintenanceRequest> Visible()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return db.MaintenanceRequests.AsNoTracking().Where(r => r.Tenancy!.TenantId == user.UserId
            && r.Tenancy.Status == TenancyStatus.Active && r.Tenancy.StartDate <= today
            && (r.Tenancy.EndDate == null || r.Tenancy.EndDate >= today)
            && (r.Tenancy.InvitedUtc == null || r.Tenancy.AcceptedUtc != null));
    }

    private static IQueryable<TenantMaintenanceItem> Project(IQueryable<MaintenanceRequest> query) =>
        query.Select(r => new TenantMaintenanceItem(r.Id, r.Tenancy!.Property!.Title,
            r.Tenancy.Property.Address, r.Description, DateTime.SpecifyKind(r.DateSubmitted, DateTimeKind.Utc),
            DateTime.SpecifyKind(r.LastUpdatedUtc, DateTimeKind.Utc), r.Status.ToString(), r.Priority.ToString()));

    public Task<TenantMaintenanceResult<int>> CreateAsync(string? description, CancellationToken cancellationToken) =>
        ExecuteAsync<int>(async () =>
        {
            var text = description?.Trim();
            if (text is null || text.Length is < 10 or > 2000) return new(TenantMaintenanceStatus.Invalid);
            var today = DateOnly.FromDateTime(clock.UtcNow);
            var ids = await db.Tenancies.AsNoTracking().Where(t => t.TenantId == user.UserId
                && t.Status == TenancyStatus.Active && t.StartDate <= today && (t.EndDate == null || t.EndDate >= today)
                && (t.InvitedUtc == null || t.AcceptedUtc != null))
                .OrderBy(t => t.Id).Select(t => t.Id).Take(2).ToListAsync(cancellationToken);
            if (ids.Count == 0) return new(TenantMaintenanceStatus.NoActiveTenancy);
            if (ids.Count > 1) return new(TenantMaintenanceStatus.Conflict);
            var result = await commands.CreateAsync(new(ids[0], text), cancellationToken);
            return new(result.Status switch
            {
                MaintenanceOutcomeStatus.Success => TenantMaintenanceStatus.Success,
                MaintenanceOutcomeStatus.Validation => TenantMaintenanceStatus.NoActiveTenancy,
                MaintenanceOutcomeStatus.Unauthenticated => TenantMaintenanceStatus.Unauthenticated,
                MaintenanceOutcomeStatus.Forbidden => TenantMaintenanceStatus.Forbidden,
                _ => TenantMaintenanceStatus.Conflict
            }, result.RequestId ?? 0);
        });

    public Task<TenantMaintenanceResult<TenantMaintenancePage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        ExecuteAsync<TenantMaintenancePage>(async () =>
        {
            if (page is < 1 or > 10000 || pageSize is < 1 or > 100) return new(TenantMaintenanceStatus.Invalid);
            var query = Visible();
            var total = await query.CountAsync(cancellationToken);
            var items = await Project(query.OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
            return new(TenantMaintenanceStatus.Success, new(items, total));
        });

    public Task<TenantMaintenanceResult<TenantMaintenanceDetail>> GetAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync<TenantMaintenanceDetail>(async () =>
        {
            var item = await Project(Visible().Where(r => r.Id == id)).SingleOrDefaultAsync(cancellationToken);
            if (item is null) return new(TenantMaintenanceStatus.NotFound);
            // Scope the event query too, so ending a tenancy between reads cannot expose history.
            var history = await db.MaintenanceStatusEvents.AsNoTracking()
                .Where(e => e.MaintenanceRequestId == id && Visible().Any(r => r.Id == e.MaintenanceRequestId))
                .OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id)
                .Select(e => new MaintenanceHistoryItem(e.Status.ToString(), DateTime.SpecifyKind(e.OccurredUtc, DateTimeKind.Utc), e.IsLegacyBaseline, e.PreviousStatus == null ? null : e.PreviousStatus.ToString(), e.Note))
                .ToListAsync(cancellationToken);
            return new(TenantMaintenanceStatus.Success, new(item, history));
        });

    private async Task<TenantMaintenanceResult<T>> ExecuteAsync<T>(Func<Task<TenantMaintenanceResult<T>>> action)
    {
        if (Access != TenantMaintenanceStatus.Success) return new(Access);
        try { return await action(); }
        catch (Exception exception) when (exception is DbException or DbUpdateException or TimeoutException
            || exception is InvalidOperationException { InnerException: DbException or TimeoutException })
        {
            // Do not log database exceptions here: they can contain submitted content.
            return new(TenantMaintenanceStatus.Unavailable);
        }
    }
}
