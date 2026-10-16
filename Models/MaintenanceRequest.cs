using System.ComponentModel.DataAnnotations;

namespace RentRight.Models;

public class MaintenanceRequest
{
    public ICollection<MaintenanceStatusEvent> StatusHistory { get; set; } = [];

    public int Id { get; set; }
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;
    public long Revision { get; set; }

    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public MaintenanceRequestStatus Status { get; set; } = MaintenanceRequestStatus.Submitted;

    [Range(1, int.MaxValue)]
    public int TenancyId { get; set; }
    public Tenancy? Tenancy { get; set; }
}
