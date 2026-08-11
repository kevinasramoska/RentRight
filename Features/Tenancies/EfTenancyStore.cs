using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;

namespace RentRight.Features.Tenancies;

public sealed class EfTenancyStore(ApplicationDbContext dbContext) : ITenancyStore
{
    private readonly Dictionary<int, Tenancy> _loadedTenancies = [];

    public async Task<IReadOnlyList<TenancyAdminRow>> ListForAdministrationAsync(
        int maximumResults,
        CancellationToken cancellationToken) =>
        await dbContext.Tenancies
            .AsNoTracking()
            .OrderByDescending(tenancy => tenancy.Status == TenancyStatus.Active)
            .ThenBy(tenancy => tenancy.Property!.Title)
            .ThenBy(tenancy => tenancy.Id)
            .Take(maximumResults)
            .Select(tenancy => new TenancyAdminRow(
                tenancy.Id,
                tenancy.Property!.Title,
                tenancy.Property.Address,
                tenancy.Tenant!.FullName,
                tenancy.Tenant.Email ?? string.Empty,
                tenancy.StartDate,
                tenancy.EndDate,
                tenancy.Status))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(
        int maximumResults,
        CancellationToken cancellationToken) =>
        await dbContext.Properties
            .AsNoTracking()
            .Where(property => !property.Tenancies.Any(tenancy =>
                tenancy.Status == TenancyStatus.Active))
            .OrderBy(property => property.Title)
            .ThenBy(property => property.Id)
            .Take(maximumResults)
            .Select(property => new TenancySelectionOption(
                property.Id.ToString(),
                property.Title + " — " + property.Address))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenancySelectionOption>> ListTenantAccountsAsync(
        int maximumResults,
        CancellationToken cancellationToken)
    {
        const string normalizedTenantRole = "TENANT";

        return await (
                from user in dbContext.Users.AsNoTracking()
                join userRole in dbContext.UserRoles.AsNoTracking()
                    on user.Id equals userRole.UserId
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where role.NormalizedName == normalizedTenantRole
                orderby user.FullName, user.Email, user.Id
                select new TenancySelectionOption(
                    user.Id,
                    string.IsNullOrWhiteSpace(user.FullName)
                        ? user.Email ?? user.UserName ?? user.Id
                        : user.FullName + " — " + user.Email))
            .Take(maximumResults)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> PropertyExistsAsync(
        int propertyId,
        CancellationToken cancellationToken) =>
        dbContext.Properties
            .AsNoTracking()
            .AnyAsync(property => property.Id == propertyId, cancellationToken);

    public Task<bool> TenantAccountExistsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        const string normalizedTenantRole = "TENANT";

        return (
                from user in dbContext.Users.AsNoTracking()
                join userRole in dbContext.UserRoles.AsNoTracking()
                    on user.Id equals userRole.UserId
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where user.Id == tenantId && role.NormalizedName == normalizedTenantRole
                select user.Id)
            .AnyAsync(cancellationToken);
    }

    public Task<bool> HasActiveTenancyAsync(
        int propertyId,
        CancellationToken cancellationToken) =>
        dbContext.Tenancies
            .AsNoTracking()
            .AnyAsync(
                tenancy => tenancy.PropertyId == propertyId &&
                    tenancy.Status == TenancyStatus.Active,
                cancellationToken);

    public async Task<CreateTenancyPersistenceResult> CreateAsync(
        CreateTenancyRecord tenancy,
        CancellationToken cancellationToken)
    {
        var entity = new Tenancy
        {
            PropertyId = tenancy.PropertyId,
            TenantId = tenancy.TenantId,
            StartDate = tenancy.StartDate,
            EndDate = tenancy.EndDate,
            Status = TenancyStatus.Active
        };

        dbContext.Tenancies.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CreateTenancyPersistenceResult.Success;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            var activeTenancyNowExists = await HasActiveTenancyAsync(
                tenancy.PropertyId,
                cancellationToken);

            if (!activeTenancyNowExists)
            {
                throw;
            }

            return CreateTenancyPersistenceResult.ActiveTenancyConflict;
        }
    }

    public async Task<TenancyEndState?> GetForEndAsync(
        int tenancyId,
        CancellationToken cancellationToken)
    {
        var tenancy = await dbContext.Tenancies
            .SingleOrDefaultAsync(item => item.Id == tenancyId, cancellationToken);
        if (tenancy is null)
        {
            return null;
        }

        _loadedTenancies[tenancy.Id] = tenancy;
        return new TenancyEndState(tenancy.Id, tenancy.StartDate, tenancy.Status);
    }

    public async Task<EndTenancyPersistenceResult> SaveEndAsync(
        int tenancyId,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (!_loadedTenancies.TryGetValue(tenancyId, out var tenancy))
        {
            throw new InvalidOperationException("The tenancy must be loaded before it is ended.");
        }

        tenancy.Status = TenancyStatus.Ended;
        tenancy.EndDate = endDate;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return EndTenancyPersistenceResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EndTenancyPersistenceResult.Conflict;
        }
    }
}
