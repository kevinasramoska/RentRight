using System.ComponentModel.DataAnnotations;

namespace RentRight.Models;

public class MaintenanceRequest
{
    public int Id { get; set; }
    [Required] public string Description { get; set; } = string.Empty;
    public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";

    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    public string TenantId { get; set; } = string.Empty;
    public ApplicationUser? Tenant { get; set; }
}