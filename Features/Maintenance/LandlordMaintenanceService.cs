using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Authorization;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class MaintenanceQueueQuery
{
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(20)] public string? Status { get; set; }
    [Range(1, int.MaxValue)] public int? PropertyId { get; set; }
    public DateOnly? SubmittedFrom { get; set; }
    public DateOnly? SubmittedTo { get; set; }
}
public sealed record MaintenanceQueueItem(int Id, int PropertyId, string PropertyTitle,
    DateTime SubmittedUtc, string Status, string Priority, long Revision);
public sealed record MaintenanceQueuePage(IReadOnlyList<MaintenanceQueueItem> Items, int TotalCount);
public sealed record MaintenanceAuditItem(string Status, string? PreviousStatus, DateTime OccurredUtc,
    string? ActorUserId, string? Note, bool IsLegacyBaseline);
public sealed record MaintenanceTriageDetail(MaintenanceQueueItem Request, string Description,
    IReadOnlyList<string> AllowedStatuses, IReadOnlyList<MaintenanceAuditItem> History);

public interface ILandlordMaintenanceService
{
    Task<TenantMaintenanceResult<MaintenanceQueuePage>> ListAsync(MaintenanceQueueQuery query, CancellationToken cancellationToken);
    Task<TenantMaintenanceResult<MaintenanceTriageDetail>> GetAsync(int id, CancellationToken cancellationToken);
}

public sealed class LandlordMaintenanceService(ApplicationDbContext db, ICurrentUser user) : ILandlordMaintenanceService
{
    private IQueryable<MaintenanceRequest> Visible() => db.MaintenanceRequests.AsNoTracking()
        .Where(r => user.IsInRole(AppRoles.Admin) || r.Tenancy!.Property!.LandlordId == user.UserId);

    private static IQueryable<MaintenanceQueueItem> Project(IQueryable<MaintenanceRequest> query) => query.Select(r =>
        new MaintenanceQueueItem(r.Id, r.Tenancy!.PropertyId, r.Tenancy.Property!.Title,
            DateTime.SpecifyKind(r.DateSubmitted, DateTimeKind.Utc), r.Status.ToString(), r.Priority.ToString(), r.Revision));

    public Task<TenantMaintenanceResult<MaintenanceQueuePage>> ListAsync(MaintenanceQueueQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync<MaintenanceQueuePage>(async () =>
        {
            MaintenanceRequestStatus? status = null;
            if (query.Status is not null)
            {
                // Named values only; numeric enum strings are not filter contracts.
                if (query.Status.Length > 20 || !Enum.GetNames<MaintenanceRequestStatus>().Contains(query.Status))
                    return new(TenantMaintenanceStatus.Invalid);
                status = Enum.Parse<MaintenanceRequestStatus>(query.Status);
            }
            if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 || query.PropertyId <= 0
                || query.SubmittedFrom > query.SubmittedTo) return new(TenantMaintenanceStatus.Invalid);
            var rows = Visible();
            if (status.HasValue) rows = rows.Where(r => r.Status == status);
            if (query.PropertyId.HasValue) rows = rows.Where(r => r.Tenancy!.PropertyId == query.PropertyId);
            if (query.SubmittedFrom.HasValue)
            {
                var from = query.SubmittedFrom.Value.ToDateTime(TimeOnly.MinValue);
                rows = rows.Where(r => r.DateSubmitted >= from);
            }
            if (query.SubmittedTo.HasValue && query.SubmittedTo.Value < DateOnly.MaxValue)
            {
                var until = query.SubmittedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                rows = rows.Where(r => r.DateSubmitted < until);
            }
            var count = await rows.CountAsync(cancellationToken);
            var items = await Project(rows.OrderByDescending(r => r.DateSubmitted).ThenByDescending(r => r.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)).ToListAsync(cancellationToken);
            return new(TenantMaintenanceStatus.Success, new(items, count));
        });

    public Task<TenantMaintenanceResult<MaintenanceTriageDetail>> GetAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync<MaintenanceTriageDetail>(async () =>
        {
            var row = await Visible().Where(r => r.Id == id)
                .Select(r => new { r.Description, r.Status,
                    Item = new MaintenanceQueueItem(r.Id, r.Tenancy!.PropertyId, r.Tenancy.Property!.Title,
                        DateTime.SpecifyKind(r.DateSubmitted, DateTimeKind.Utc), r.Status.ToString(), r.Priority.ToString(), r.Revision) })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null) return new(TenantMaintenanceStatus.NotFound);
            var history = await db.MaintenanceStatusEvents.AsNoTracking()
                .Where(e => e.MaintenanceRequestId == id && Visible().Any(r => r.Id == e.MaintenanceRequestId))
                .OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id)
                .Select(e => new MaintenanceAuditItem(e.Status.ToString(), e.PreviousStatus == null ? null : e.PreviousStatus.ToString(),
                    DateTime.SpecifyKind(e.OccurredUtc, DateTimeKind.Utc), e.ActorUserId, e.Note, e.IsLegacyBaseline)).ToListAsync(cancellationToken);
            return new(TenantMaintenanceStatus.Success, new(row.Item, row.Description,
                row.Status.GetAllowedTransitions().Select(s => s.ToString()).ToArray(), history));
        });

    private async Task<TenantMaintenanceResult<T>> ExecuteAsync<T>(Func<Task<TenantMaintenanceResult<T>>> action)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) return new(TenantMaintenanceStatus.Unauthenticated);
        if (!user.IsInRole(AppRoles.Admin) && !user.IsInRole(AppRoles.Landlord)) return new(TenantMaintenanceStatus.Forbidden);
        try { return await action(); }
        catch (Exception exception) when (exception is DbException or DbUpdateException or TimeoutException
            || exception is InvalidOperationException { InnerException: DbException or TimeoutException })
        { return new(TenantMaintenanceStatus.Unavailable); }
    }
}
