using System.ComponentModel.DataAnnotations;
using RentRight.Api;

namespace RentRight.Controllers.Api.V1;

public sealed class PropertyListRequest : IValidatableObject
{
    private const string MaximumRentValue = "9999999999999999.99";

    [Range(1, ApiConventions.MaximumPage)]
    public int Page { get; init; } = ApiConventions.DefaultPage;

    [Range(1, ApiConventions.MaximumPageSize)]
    public int PageSize { get; init; } = ApiConventions.DefaultPageSize;

    [Required]
    [RegularExpression("(?i)^published$")]
    public string Status { get; init; } = "published";

    [StringLength(200)]
    public string? Search { get; init; }

    [Range(typeof(decimal), "0", MaximumRentValue)]
    public decimal? MinimumRent { get; init; }

    [Range(typeof(decimal), "0", MaximumRentValue)]
    public decimal? MaximumRent { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinimumRent is decimal minimumRent &&
            MaximumRent is decimal maximumRent &&
            minimumRent > maximumRent)
        {
            yield return new ValidationResult(
                "Minimum rent cannot exceed maximum rent.",
                [nameof(MinimumRent), nameof(MaximumRent)]);
        }
    }
}

public sealed class PropertyCreateRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal RentAmount { get; init; }

    [StringLength(2000)]
    public string? Description { get; init; }
}

public sealed class PropertyUpdateRequest
{
    public string? ExpectedVersion { get; init; }

    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal RentAmount { get; init; }

    [StringLength(2000)]
    public string? Description { get; init; }
}

public sealed record PropertySummaryResponse(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string? Description);

public sealed record PropertyResponse(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string? Description);
