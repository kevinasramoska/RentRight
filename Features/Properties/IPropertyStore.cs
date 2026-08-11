using RentRight.Features.Properties.Contracts;

namespace RentRight.Features.Properties;

public interface IPropertyStore
{
    Task<PublishedPropertyPageRecord> SearchPublishedAsync(
        PublishedPropertyQuery query,
        CancellationToken cancellationToken);

    Task<PublishedPropertyListItem?> FindPublishedAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PropertyListRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LatestPropertyListItem>> ListLatestAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<PropertyRecord?> FindAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<int> CreateAsync(
        PropertyValues values,
        string landlordId,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        int propertyId,
        PropertyValues values,
        CancellationToken cancellationToken);

    Task<PropertyDeleteStoreStatus> DeleteAsync(
        int propertyId,
        CancellationToken cancellationToken);
}

public sealed record PropertyListRecord(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string LandlordId);

public sealed record PropertyRecord(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string? Description,
    string LandlordId);

public sealed record PropertyValues(
    string Title,
    string Address,
    decimal RentAmount,
    string? Description);

public sealed record PublishedPropertyPageRecord(
    IReadOnlyList<PublishedPropertyListItem> Items,
    int TotalCount);

public enum PropertyDeleteStoreStatus
{
    Deleted,
    NotFound,
    HasHistory,
    Conflict
}
