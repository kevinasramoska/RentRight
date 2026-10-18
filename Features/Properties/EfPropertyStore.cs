using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Features.Properties.Contracts;
using RentRight.Models;

namespace RentRight.Features.Properties;

public sealed class EfPropertyStore(ApplicationDbContext context) : IPropertyStore
{
    public async Task<PublishedPropertyPageRecord> SearchPublishedAsync(
        PublishedPropertyQuery query,
        CancellationToken cancellationToken)
    {
        // The current model has no draft/archive state, so every persisted row is published.
        var properties = context.Properties.AsNoTracking();

        if (query.Search is not null)
        {
            properties = properties.Where(property =>
                property.Title.Contains(query.Search) ||
                property.Address.Contains(query.Search));
        }

        if (query.MinimumRent is decimal minimumRent)
        {
            properties = properties.Where(property => property.RentAmount >= minimumRent);
        }

        if (query.MaximumRent is decimal maximumRent)
        {
            properties = properties.Where(property => property.RentAmount <= maximumRent);
        }

        var totalCount = await properties.CountAsync(cancellationToken);
        var items = await properties
            .OrderBy(property => property.Title)
            .ThenBy(property => property.Address)
            .ThenBy(property => property.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(property => new PublishedPropertyListItem(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.Description))
            .ToListAsync(cancellationToken);

        return new PublishedPropertyPageRecord(items, totalCount);
    }

    public Task<PublishedPropertyListItem?> FindPublishedAsync(
        int propertyId,
        CancellationToken cancellationToken) =>
        context.Properties
            .AsNoTracking()
            .Where(property => property.Id == propertyId)
            .Select(property => new PublishedPropertyListItem(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.Description))
            .SingleOrDefaultAsync(cancellationToken);

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

    public async Task<IReadOnlyList<LatestPropertyListItem>> ListLatestAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await context.Properties
            .AsNoTracking()
            .OrderByDescending(property => property.Id)
            .Take(limit)
            .Select(property => new LatestPropertyListItem(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                property.Description))
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
                property.LandlordId,
                Convert.ToBase64String(property.RowVersion),
                property.Latitude != null && property.Longitude != null && property.GeocodedUtc != null
                    ? new PropertyLocation(property.Latitude.Value, property.Longitude.Value, property.GeocodedUtc.Value) : null))
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
        return await context.Properties
            .Where(property => property.Id == propertyId && property.LandlordId == values.ExpectedLandlordId
                && property.RowVersion == values.ExpectedVersion)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(property => property.Title, values.Title)
                .SetProperty(property => property.Address, values.Address)
                .SetProperty(property => property.RentAmount, values.RentAmount)
                .SetProperty(property => property.Description, values.Description)
                // Derived from the version-checked snapshot, not a posted flag or SQL's padded string equality.
                .SetProperty(property => property.Latitude, property => values.AddressChanged ? null : property.Latitude)
                .SetProperty(property => property.Longitude, property => values.AddressChanged ? null : property.Longitude)
                .SetProperty(property => property.GeocodingPlaceId, property => values.AddressChanged ? null : property.GeocodingPlaceId)
                .SetProperty(property => property.GeocodedUtc, property => values.AddressChanged ? null : property.GeocodedUtc), cancellationToken) == 1;
    }

    public async Task<PropertyDeleteStoreStatus> DeleteAsync(
        int propertyId,
        CancellationToken cancellationToken,
        byte[] expectedVersion,
        string expectedLandlordId)
    {
        var property = await context.Properties
            .SingleOrDefaultAsync(item => item.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return PropertyDeleteStoreStatus.NotFound;
        }

        if (property.LandlordId != expectedLandlordId || !property.RowVersion.SequenceEqual(expectedVersion))
            return PropertyDeleteStoreStatus.Conflict;

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
