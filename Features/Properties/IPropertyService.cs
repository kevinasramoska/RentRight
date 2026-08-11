using RentRight.Features.Properties.Contracts;

namespace RentRight.Features.Properties;

public interface IPropertyService
{
    Task<PropertyQueryOutcome<PropertyPage<PublishedPropertyListItem>>> SearchPublishedAsync(
        PublishedPropertyQuery query,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<PublishedPropertyDetails>> GetPublishedDetailsAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>> ListAsync(
        PropertyListQuery query,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>> ListLatestAsync(
        LatestPropertiesQuery query,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<PropertyDetails>> GetDetailsAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<PropertyMutationOutcome> CreateAsync(
        PropertyCreateInput input,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<PropertyEditInput>> GetEditAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<PropertyMutationOutcome> UpdateAsync(
        int propertyId,
        PropertyEditInput input,
        CancellationToken cancellationToken);

    Task<PropertyQueryOutcome<PropertyDetails>> GetDeleteAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<PropertyMutationOutcome> DeleteAsync(
        int propertyId,
        CancellationToken cancellationToken);
}
