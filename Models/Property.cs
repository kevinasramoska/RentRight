using System.ComponentModel.DataAnnotations;

namespace RentRight.Models;

public class Property
{
    public int Id { get; set; }
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Address { get; set; } = string.Empty;

    [Range(0, double.MaxValue)] public decimal RentAmount { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public string LandlordId { get; set; } = string.Empty;
    public ApplicationUser? Landlord { get; set; }

    public ICollection<Tenancy> Tenancies { get; set; } = [];
}
