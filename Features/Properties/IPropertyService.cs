using RentRight.Features.Properties.Contracts;

namespace RentRight.Features.Properties;

public interface IPropertyService
{
    Task<PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>> ListAsync(
        PropertyListQuery query,
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
