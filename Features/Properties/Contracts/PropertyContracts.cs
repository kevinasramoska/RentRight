using System.ComponentModel.DataAnnotations;

namespace RentRight.Features.Properties.Contracts;

public sealed record PropertyListQuery(int Limit = 100)
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 100;
}

public sealed record PropertyListItem(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    bool CanManage);

public sealed record LatestPropertiesQuery(int Limit = 6)
{
    public const int DefaultLimit = 6;
    public const int MaximumLimit = 12;
}

public sealed record LatestPropertyListItem(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string? Description);

public sealed record PropertyDetails(
    int Id,
    string Title,
    string Address,
    decimal RentAmount,
    string? Description,
    bool CanManage);

public sealed class PropertyCreateInput
{
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; set; } = string.Empty;

    [Display(Name = "Rent Amount")]
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal RentAmount { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }
}

public sealed class PropertyEditInput
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; set; } = string.Empty;

    [Display(Name = "Rent Amount")]
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal RentAmount { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }
}

public enum PropertyOperationStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    ValidationFailed,
    Conflict,
    HasHistory
}

public sealed record PropertyQueryOutcome<T>(
    PropertyOperationStatus Status,
    T? Value = default)
    where T : class
{
    public static PropertyQueryOutcome<T> Success(T value) =>
        new(PropertyOperationStatus.Success, value);

    public static PropertyQueryOutcome<T> Unauthenticated() =>
        new(PropertyOperationStatus.Unauthenticated);

    public static PropertyQueryOutcome<T> Forbidden() =>
        new(PropertyOperationStatus.Forbidden);

    public static PropertyQueryOutcome<T> NotFound() =>
        new(PropertyOperationStatus.NotFound);
}

public sealed record PropertyMutationOutcome(
    PropertyOperationStatus Status,
    int? PropertyId = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static PropertyMutationOutcome Success(int propertyId) =>
        new(PropertyOperationStatus.Success, propertyId);

    public static PropertyMutationOutcome Unauthenticated() =>
        new(PropertyOperationStatus.Unauthenticated);

    public static PropertyMutationOutcome Forbidden() =>
        new(PropertyOperationStatus.Forbidden);

    public static PropertyMutationOutcome NotFound() =>
        new(PropertyOperationStatus.NotFound);

    public static PropertyMutationOutcome ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(PropertyOperationStatus.ValidationFailed, ValidationErrors: errors);

    public static PropertyMutationOutcome Conflict() =>
        new(PropertyOperationStatus.Conflict);

    public static PropertyMutationOutcome HasHistory() =>
        new(PropertyOperationStatus.HasHistory);
}
