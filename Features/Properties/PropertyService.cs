using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Features.Properties.Contracts;

namespace RentRight.Features.Properties;

public sealed class PropertyService(
    IPropertyStore propertyStore,
    ICurrentUser currentUser) : IPropertyService
{
    private const string AdministratorRole = "Admin";
    private const string LandlordRole = "Landlord";

    public async Task<PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>> ListAsync(
        PropertyListQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, PropertyListQuery.MaximumLimit);
        var properties = await propertyStore.ListAsync(limit, cancellationToken);

        var items = properties
            .Select(property => new PropertyListItem(
                property.Id,
                property.Title,
                property.Address,
                property.RentAmount,
                CanManage(property.LandlordId)))
            .ToArray();

        return PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>.Success(items);
    }

    public async Task<PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>> ListLatestAsync(
        LatestPropertiesQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, LatestPropertiesQuery.MaximumLimit);
        var properties = await propertyStore.ListLatestAsync(limit, cancellationToken);

        return PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>.Success(properties);
    }

    public async Task<PropertyQueryOutcome<PropertyDetails>> GetDetailsAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var property = await propertyStore.FindAsync(propertyId, cancellationToken);
        return property is null
            ? PropertyQueryOutcome<PropertyDetails>.NotFound()
            : PropertyQueryOutcome<PropertyDetails>.Success(ToDetails(property));
    }

    public async Task<PropertyMutationOutcome> CreateAsync(
        PropertyCreateInput input,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetManagerAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var validationFailure = Validate(input);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        try
        {
            var propertyId = await propertyStore.CreateAsync(
                ToValues(input.Title, input.Address, input.RentAmount, input.Description),
                currentUser.UserId!,
                cancellationToken);
            return PropertyMutationOutcome.Success(propertyId);
        }
        catch (DbUpdateException)
        {
            return PropertyMutationOutcome.Conflict();
        }
    }

    public async Task<PropertyQueryOutcome<PropertyEditInput>> GetEditAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var property = await FindAuthorizedAsync(propertyId, cancellationToken);
        return property.Status switch
        {
            PropertyOperationStatus.Success =>
                PropertyQueryOutcome<PropertyEditInput>.Success(new PropertyEditInput
                {
                    Id = property.Value!.Id,
                    Title = property.Value.Title,
                    Address = property.Value.Address,
                    RentAmount = property.Value.RentAmount,
                    Description = property.Value.Description
                }),
            PropertyOperationStatus.Unauthenticated =>
                PropertyQueryOutcome<PropertyEditInput>.Unauthenticated(),
            PropertyOperationStatus.Forbidden =>
                PropertyQueryOutcome<PropertyEditInput>.Forbidden(),
            PropertyOperationStatus.NotFound =>
                PropertyQueryOutcome<PropertyEditInput>.NotFound(),
            _ => throw new InvalidOperationException("Unknown property authorization outcome.")
        };
    }

    public async Task<PropertyMutationOutcome> UpdateAsync(
        int propertyId,
        PropertyEditInput input,
        CancellationToken cancellationToken)
    {
        var property = await FindAuthorizedAsync(propertyId, cancellationToken);
        var authorizationFailure = ToMutationFailure(property.Status);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var validationFailure = Validate(input);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        try
        {
            var updated = await propertyStore.UpdateAsync(
                propertyId,
                ToValues(input.Title, input.Address, input.RentAmount, input.Description),
                cancellationToken);
            return updated
                ? PropertyMutationOutcome.Success(propertyId)
                : PropertyMutationOutcome.NotFound();
        }
        catch (DbUpdateException)
        {
            return PropertyMutationOutcome.Conflict();
        }
    }

    public async Task<PropertyQueryOutcome<PropertyDetails>> GetDeleteAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var property = await FindAuthorizedAsync(propertyId, cancellationToken);
        return property.Status switch
        {
            PropertyOperationStatus.Success =>
                PropertyQueryOutcome<PropertyDetails>.Success(ToDetails(property.Value!)),
            PropertyOperationStatus.Unauthenticated =>
                PropertyQueryOutcome<PropertyDetails>.Unauthenticated(),
            PropertyOperationStatus.Forbidden =>
                PropertyQueryOutcome<PropertyDetails>.Forbidden(),
            PropertyOperationStatus.NotFound =>
                PropertyQueryOutcome<PropertyDetails>.NotFound(),
            _ => throw new InvalidOperationException("Unknown property authorization outcome.")
        };
    }

    public async Task<PropertyMutationOutcome> DeleteAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var property = await FindAuthorizedAsync(propertyId, cancellationToken);
        var authorizationFailure = ToMutationFailure(property.Status);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var status = await propertyStore.DeleteAsync(propertyId, cancellationToken);
        return status switch
        {
            PropertyDeleteStoreStatus.Deleted => PropertyMutationOutcome.Success(propertyId),
            PropertyDeleteStoreStatus.NotFound => PropertyMutationOutcome.NotFound(),
            PropertyDeleteStoreStatus.HasHistory => PropertyMutationOutcome.HasHistory(),
            PropertyDeleteStoreStatus.Conflict => PropertyMutationOutcome.Conflict(),
            _ => throw new InvalidOperationException("Unknown property deletion outcome.")
        };
    }

    private async Task<PropertyQueryOutcome<PropertyRecord>> FindAuthorizedAsync(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetManagerAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure.Status switch
            {
                PropertyOperationStatus.Unauthenticated =>
                    PropertyQueryOutcome<PropertyRecord>.Unauthenticated(),
                PropertyOperationStatus.Forbidden =>
                    PropertyQueryOutcome<PropertyRecord>.Forbidden(),
                _ => throw new InvalidOperationException("Unknown manager authorization outcome.")
            };
        }

        var property = await propertyStore.FindAsync(propertyId, cancellationToken);
        if (property is null)
        {
            return PropertyQueryOutcome<PropertyRecord>.NotFound();
        }

        return CanManage(property.LandlordId)
            ? PropertyQueryOutcome<PropertyRecord>.Success(property)
            : PropertyQueryOutcome<PropertyRecord>.Forbidden();
    }

    private PropertyMutationOutcome? GetManagerAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return PropertyMutationOutcome.Unauthenticated();
        }

        if (currentUser.IsInRole(AdministratorRole))
        {
            return null;
        }

        return currentUser.IsInRole(LandlordRole)
            ? null
            : PropertyMutationOutcome.Forbidden();
    }

    private bool CanManage(string landlordId)
    {
        if (currentUser.IsInRole(AdministratorRole))
        {
            return true;
        }

        return currentUser.IsInRole(LandlordRole) &&
            string.Equals(landlordId, currentUser.UserId, StringComparison.Ordinal);
    }

    private static PropertyMutationOutcome? ToMutationFailure(PropertyOperationStatus status) =>
        status switch
        {
            PropertyOperationStatus.Success => null,
            PropertyOperationStatus.Unauthenticated => PropertyMutationOutcome.Unauthenticated(),
            PropertyOperationStatus.Forbidden => PropertyMutationOutcome.Forbidden(),
            PropertyOperationStatus.NotFound => PropertyMutationOutcome.NotFound(),
            _ => throw new InvalidOperationException("Unknown property query outcome.")
        };

    private PropertyDetails ToDetails(PropertyRecord property) =>
        new(
            property.Id,
            property.Title,
            property.Address,
            property.RentAmount,
            property.Description,
            CanManage(property.LandlordId));

    private static PropertyValues ToValues(
        string title,
        string address,
        decimal rentAmount,
        string? description) =>
        new(title, address, rentAmount, description);

    private static PropertyMutationOutcome? Validate(object input)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(
                input,
                new ValidationContext(input),
                results,
                validateAllProperties: true))
        {
            return null;
        }

        var errors = results
            .SelectMany(result =>
            {
                var memberNames = result.MemberNames.Any()
                    ? result.MemberNames
                    : [string.Empty];
                return memberNames.Select(memberName => new
                {
                    MemberName = memberName,
                    Error = result.ErrorMessage ?? "The value is invalid."
                });
            })
            .GroupBy(error => error.MemberName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Error).ToArray(),
                StringComparer.Ordinal);

        return PropertyMutationOutcome.ValidationFailed(errors);
    }
}
