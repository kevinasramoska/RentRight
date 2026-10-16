using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;

namespace RentRight.Features.Tenancies;

public sealed class EfTenancyStore(ApplicationDbContext db, ILookupNormalizer normalizer) : ITenancyStore
{
    private IQueryable<Tenancy> Visible(TenancyActor actor, DateOnly today) => db.Tenancies.AsNoTracking()
        .Where(t => actor.IsAdmin || (actor.IsLandlord && t.Property!.LandlordId == actor.UserId)
            || (actor.IsTenant && t.TenantId == actor.UserId && t.Status == TenancyStatus.Active
                && t.StartDate <= today && (t.EndDate == null || t.EndDate >= today)));

    private static IQueryable<TenancyAdminRow> Project(IQueryable<Tenancy> query) => query.Select(t => new TenancyAdminRow(
        t.Id, t.Property!.Title, t.Property.Address,
        // Pending/cancelled invitation data must not reveal account existence.
        t.AcceptedUtc != null || (t.InvitedUtc == null && t.TenantId != null) ? t.Tenant!.FullName : string.Empty,
        t.AcceptedUtc != null || (t.InvitedUtc == null && t.TenantId != null) ? t.Tenant!.Email ?? string.Empty : string.Empty,
        t.StartDate, t.EndDate, t.Status));

    public async Task<IReadOnlyList<TenancyAdminRow>> ListAsync(TenancyActor actor, DateOnly today, int skip, int take, CancellationToken cancellationToken) =>
        await Project(Visible(actor, today).OrderByDescending(t => t.Id).Skip(skip).Take(take)).ToListAsync(cancellationToken);

    public Task<int> CountAsync(TenancyActor actor, DateOnly today, CancellationToken cancellationToken) =>
        Visible(actor, today).CountAsync(cancellationToken);

    public Task<TenancyAdminRow?> GetAsync(int id, TenancyActor actor, DateOnly today, CancellationToken cancellationToken) =>
        Project(Visible(actor, today).Where(t => t.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(TenancyActor actor, DateTime now, int limit, CancellationToken cancellationToken) =>
        await db.Properties.AsNoTracking().Where(p => actor.IsAdmin || p.LandlordId == actor.UserId)
            .Where(p => !p.Tenancies.Any(t => t.Status == TenancyStatus.Active
                || (t.Status == TenancyStatus.Pending && t.InvitationExpiresUtc > now)))
            .OrderBy(p => p.Title).ThenBy(p => p.Id).Take(limit)
            .Select(p => new TenancySelectionOption(p.Id.ToString(), p.Title + " — " + p.Address)).ToListAsync(cancellationToken);

    // Every lifecycle mutation first locks the same persisted property row. This serializes
    // invite/accept/end and ownership/delete changes, including cross-operation races.
    private Task<Property?> LockPropertyAsync(int id, CancellationToken cancellationToken) =>
        db.Properties.FromSqlInterpolated($"SELECT * FROM [Properties] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private static bool CanManage(Property property, TenancyActor actor) =>
        actor.IsAdmin || (actor.IsLandlord && property.LandlordId == actor.UserId);

    private Task<bool> IsTenantAsync(string id, CancellationToken cancellationToken) =>
        (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
         where ur.UserId == id && r.NormalizedName == "TENANT" select ur.UserId).AnyAsync(cancellationToken);

    public async Task<CreateTenancyStatus> InviteAsync(InviteTenancyRecord record, TenancyActor actor, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var property = await LockPropertyAsync(record.Command.PropertyId!.Value, cancellationToken);
            if (property is null || !CanManage(property, actor)) return CreateTenancyStatus.PropertyNotFound;
            if (await db.Tenancies.AnyAsync(t => t.PropertyId == property.Id &&
                (t.Status == TenancyStatus.Active || (t.Status == TenancyStatus.Pending && t.InvitationExpiresUtc > record.UtcNow)), cancellationToken))
                return CreateTenancyStatus.Conflict;

            var expired = await db.Tenancies.Where(t => t.PropertyId == property.Id && t.Status == TenancyStatus.Pending).ToListAsync(cancellationToken);
            foreach (var old in expired)
            {
                old.Status = TenancyStatus.Ended;
                old.InvitationTokenHash = null;
                old.EndedUtc = record.UtcNow;
                old.EndReason = "Expired";
            }
            // Release the pending unique slot before inserting its replacement in this transaction.
            await db.SaveChangesAsync(cancellationToken);

            var identifier = normalizer.NormalizeName(record.Command.TenantId.Trim());
            var tenantId = await (from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.NormalizedUserName == identifier && r.NormalizedName == "TENANT"
                select u.Id).SingleOrDefaultAsync(cancellationToken);

            db.Tenancies.Add(new Tenancy
            {
                PropertyId = property.Id, TenantId = tenantId, StartDate = record.Command.StartDate,
                EndDate = record.Command.EndDate, Status = TenancyStatus.Pending,
                InvitedByUserId = actor.UserId, InvitedUtc = record.UtcNow,
                InvitationExpiresUtc = record.UtcNow.AddDays(7), InvitationTokenHash = record.TokenHash
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreateTenancyStatus.Success;
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return CreateTenancyStatus.Conflict;
        }
    }

    public async Task<AcceptTenancyStatus> AcceptAsync(string tokenHash, TenancyActor actor, DateTime now, CancellationToken cancellationToken)
    {
        // Read only a routing hint before acquiring the shared property lock; recheck all state inside it.
        var propertyId = await db.Tenancies.AsNoTracking().Where(t => t.InvitationTokenHash == tokenHash)
            .Select(t => (int?)t.PropertyId).SingleOrDefaultAsync(cancellationToken);
        if (propertyId is null) return AcceptTenancyStatus.NotFound;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (await LockPropertyAsync(propertyId.Value, cancellationToken) is null) return AcceptTenancyStatus.NotFound;
            var tenancy = await db.Tenancies.SingleOrDefaultAsync(t => t.InvitationTokenHash == tokenHash, cancellationToken);
            if (tenancy is null || tenancy.TenantId != actor.UserId || !actor.IsTenant
                || !await IsTenantAsync(actor.UserId, cancellationToken)) return AcceptTenancyStatus.NotFound;
            if (!tenancy.CanAccept(actor.UserId, now)) return AcceptTenancyStatus.Conflict;
            if (await db.Tenancies.AnyAsync(t => t.PropertyId == propertyId && t.Status == TenancyStatus.Active, cancellationToken))
                return AcceptTenancyStatus.Conflict;
            tenancy.Accept(actor.UserId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AcceptTenancyStatus.Success;
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return AcceptTenancyStatus.Conflict;
        }
    }

    public async Task<EndTenancyOutcome> EndAsync(int id, TenancyActor actor, DateTime now, CancellationToken cancellationToken)
    {
        var propertyId = await db.Tenancies.AsNoTracking().Where(t => t.Id == id).Select(t => (int?)t.PropertyId).SingleOrDefaultAsync(cancellationToken);
        if (propertyId is null) return EndTenancyOutcome.NotFound();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var property = await LockPropertyAsync(propertyId.Value, cancellationToken);
            if (property is null || !CanManage(property, actor)) return EndTenancyOutcome.NotFound();
            var tenancy = await db.Tenancies.SingleAsync(t => t.Id == id, cancellationToken);
            if (!tenancy.End(actor.UserId, now)) return EndTenancyOutcome.Conflict();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EndTenancyOutcome.Success(changed: true);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return EndTenancyOutcome.Conflict();
        }
    }

    private static bool IsConflict(Exception exception) => exception is DbUpdateConcurrencyException
        || exception is SqlException { Number: 1205 or 2601 or 2627 }
        || exception is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } };
}
