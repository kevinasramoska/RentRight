using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentRight.Models;

namespace RentRight.ViewModels;

public class TenancyAdminIndexViewModel
{
    public IReadOnlyList<TenancyAdminRowViewModel> Tenancies { get; init; } = [];
}

public class TenancyAdminRowViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public required string PropertyAddress { get; init; }
    public required string TenantName { get; init; }
    public required string TenantEmail { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public TenancyStatus Status { get; init; }
}

public class TenancyCreateViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Select a property.")]
    [Display(Name = "Property")]
    public int? PropertyId { get; set; }

    [Required(ErrorMessage = "Select a tenant.")]
    [Display(Name = "Tenant")]
    public string TenantId { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [DataType(DataType.Date)]
    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    public IReadOnlyList<SelectListItem> AvailableProperties { get; set; } = [];
    public IReadOnlyList<SelectListItem> AvailableTenants { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate is not null && EndDate < StartDate)
        {
            yield return new ValidationResult(
                "End date cannot be before the start date.",
                [nameof(EndDate)]);
        }
    }
}
