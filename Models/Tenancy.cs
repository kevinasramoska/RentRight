using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RentRight.Models;

public class Tenancy : IValidatableObject
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    public string? TenantId { get; set; }
    public ApplicationUser? Tenant { get; set; }

    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    public TenancyStatus Status { get; set; } = TenancyStatus.Active;

    // Legacy rows deliberately retain null lifecycle audit values: consent was not recorded.
    public string? InvitedByUserId { get; set; }
    public DateTime? InvitedUtc { get; set; }
    public DateTime? InvitationExpiresUtc { get; set; }
    public string? InvitationTokenHash { get; set; }
    public string? AcceptedByUserId { get; set; }
    public DateTime? AcceptedUtc { get; set; }
    public string? EndedByUserId { get; set; }
    public DateTime? EndedUtc { get; set; }
    public string? EndReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = [];

    [NotMapped]
    public bool HasValidDateRange => EndDate is null || EndDate >= StartDate;

    public bool IsActiveOn(DateOnly date) =>
        Status == TenancyStatus.Active &&
        StartDate <= date &&
        (EndDate is null || EndDate >= date);

    public bool CanAccept(string userId, DateTime now) =>
        Status == TenancyStatus.Pending && TenantId == userId && InvitationTokenHash != null &&
        InvitationExpiresUtc > now && (EndDate == null || EndDate >= DateOnly.FromDateTime(now));

    public bool Accept(string userId, DateTime now)
    {
        if (!CanAccept(userId, now)) return false;
        Status = TenancyStatus.Active;
        AcceptedByUserId = userId;
        AcceptedUtc = now;
        InvitationTokenHash = null;
        return true;
    }

    public bool End(string userId, DateTime now)
    {
        if (Status == TenancyStatus.Ended) return false;
        EndReason = Status == TenancyStatus.Pending ? "Cancelled" : "Ended";
        if (Status == TenancyStatus.Active)
        {
            var today = DateOnly.FromDateTime(now);
            EndDate = StartDate > today ? StartDate : today;
        }
        Status = TenancyStatus.Ended;
        EndedByUserId = userId;
        EndedUtc = now;
        InvitationTokenHash = null;
        return true;
    }

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
