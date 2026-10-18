using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Api;
using RentRight.Authorization;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;

namespace RentRight.Controllers.Api.V1;

public sealed class PropertiesController(IPropertyService propertyService) :
    ApiV1ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ApiPage<PropertySummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PropertyListRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await propertyService.SearchPublishedAsync(
            new PublishedPropertyQuery(
                request.Page,
                request.PageSize,
                request.Search,
                request.MinimumRent,
                request.MaximumRent),
            cancellationToken);
        if (outcome.Status != PropertyOperationStatus.Success)
        {
            return MapPropertyQueryFailure(outcome);
        }

        var page = outcome.Value!;
        return Ok(new ApiPage<PropertySummaryResponse>(
            page.Items.Select(ToSummaryResponse).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalCount));
    }

    [HttpGet("{propertyId:int}")]
    [AllowAnonymous]
    [ProducesResponseType<PropertyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        int propertyId,
        CancellationToken cancellationToken)
    {
        var outcome = await propertyService.GetPublishedDetailsAsync(
            propertyId,
            cancellationToken);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => Ok(ToResponse(outcome.Value!)),
            _ => MapPropertyQueryFailure(outcome)
        };
    }

    [HttpGet("{propertyId:int}/edit")]
    [ProducesResponseType<PropertyEditInput>(StatusCodes.Status200OK)]
    [Authorize(Policy = RentalPolicies.Manager)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetEdit(int propertyId, CancellationToken cancellationToken)
    {
        var outcome = await propertyService.GetEditAsync(propertyId, cancellationToken);
        return outcome.Status == PropertyOperationStatus.Success
            ? Ok(outcome.Value)
            : MapPropertyQueryFailure(outcome, concealForbidden: true);
    }

    [HttpPost]
    [Authorize(Policy = RentalPolicies.Manager)]
    [Consumes("application/json")]
    [ProducesResponseType<PropertyResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        PropertyCreateRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await propertyService.CreateAsync(
            new PropertyCreateInput
            {
                Title = request.Title,
                Address = request.Address,
                RentAmount = request.RentAmount,
                Description = request.Description
            },
            cancellationToken);
        if (outcome.Status != PropertyOperationStatus.Success)
        {
            return MapPropertyMutationFailure(outcome);
        }

        var response = new PropertyResponse(
            outcome.PropertyId!.Value,
            request.Title,
            request.Address,
            request.RentAmount,
            request.Description);
        return CreatedAtAction(
            nameof(Get),
            new { propertyId = response.Id },
            response);
    }

    [HttpPut("{propertyId:int}")]
    [Authorize(Policy = RentalPolicies.Manager)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        int propertyId,
        PropertyUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await propertyService.UpdateAsync(
            propertyId,
            new PropertyEditInput
            {
                Id = propertyId,
                Title = request.Title,
                Address = request.Address,
                RentAmount = request.RentAmount,
                Description = request.Description,
                ExpectedVersion = request.ExpectedVersion
            },
            cancellationToken);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => NoContent(),
            _ => MapPropertyMutationFailure(outcome, concealForbidden: true)
        };
    }

    [HttpDelete("{propertyId:int}")]
    [Authorize(Policy = RentalPolicies.Manager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        int propertyId,
        CancellationToken cancellationToken,
        [FromHeader(Name = "X-Expected-Version")] string? expectedVersion = null)
    {
        var outcome = await propertyService.DeleteAsync(propertyId, cancellationToken, expectedVersion);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => NoContent(),
            _ => MapPropertyMutationFailure(outcome, concealForbidden: true)
        };
    }

    private static PropertySummaryResponse ToSummaryResponse(
        PublishedPropertyListItem property) =>
        new(
            property.Id,
            property.Title,
            property.Address,
            property.RentAmount,
            property.Description);

    private static PropertyResponse ToResponse(PublishedPropertyDetails property) =>
        new(
            property.Id,
            property.Title,
            property.Address,
            property.RentAmount,
            property.Description);
}
