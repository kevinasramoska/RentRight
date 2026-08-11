using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RentRight.Models;

public class Tenancy : IValidatableObject
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public ApplicationUser? Tenant { get; set; }

    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    public TenancyStatus Status { get; set; } = TenancyStatus.Active;

    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = [];

    [NotMapped]
    public bool HasValidDateRange => EndDate is null || EndDate >= StartDate;

    public bool IsActiveOn(DateOnly date) =>
        Status == TenancyStatus.Active &&
        StartDate <= date &&
        (EndDate is null || EndDate >= date);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HasValidDateRange)
        {
            yield return new ValidationResult(
                "End date cannot be before the start date.",
                [nameof(EndDate)]);
        }
    }
}
