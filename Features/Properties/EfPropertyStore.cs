using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Features.Properties;

public sealed class EfPropertyStore(ApplicationDbContext context) : IPropertyStore
{
    public async Task<IReadOnlyList<PropertyListRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await context.Properties
            .AsNoTracking()
            .OrderBy(property => property.Title)
            .ThenBy(property => property.Address)
            .ThenBy(property => property.Id)
            .Take(limit)
            .Select(property => new PropertyListRecord(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.LandlordId))
            .ToListAsync(cancellationToken);

    public Task<PropertyRecord?> FindAsync(
        int propertyId,
        CancellationToken cancellationToken) =>
        context.Properties
            .AsNoTracking()
            .Where(property => property.Id == propertyId)
            .Select(property => new PropertyRecord(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.Description,
                property.LandlordId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> CreateAsync(
        PropertyValues values,
        string landlordId,
        CancellationToken cancellationToken)
    {
        var property = new Property
        {
            Title = values.Title,
            Address = values.Address,
            RentAmount = values.RentAmount,
            Description = values.Description,
            LandlordId = landlordId
        };

        context.Properties.Add(property);
        await context.SaveChangesAsync(cancellationToken);
        return property.Id;
    }

    public async Task<bool> UpdateAsync(
        int propertyId,
        PropertyValues values,
        CancellationToken cancellationToken)
    {
        var property = await context.Properties
            .SingleOrDefaultAsync(item => item.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return false;
        }

        property.Title = values.Title;
        property.Address = values.Address;
        property.RentAmount = values.RentAmount;
        property.Description = values.Description;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PropertyDeleteStoreStatus> DeleteAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var property = await context.Properties
            .SingleOrDefaultAsync(item => item.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return PropertyDeleteStoreStatus.NotFound;
        }

        if (await context.Tenancies
                .AsNoTracking()
                .AnyAsync(tenancy => tenancy.PropertyId == propertyId, cancellationToken))
        {
            return PropertyDeleteStoreStatus.HasHistory;
        }

        context.Properties.Remove(property);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return PropertyDeleteStoreStatus.Deleted;
        }
        catch (DbUpdateException)
        {
            context.Entry(property).State = EntityState.Unchanged;
            return PropertyDeleteStoreStatus.Conflict;
        }
    }
}
