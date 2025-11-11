using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RentRight.Models;

public class Property
{
    public int Id { get; set; }
    [Required] public string Title { get; set; } = string.Empty;
    [Required] public string Address { get; set; } = string.Empty;
    [Range(0, double.MaxValue)] public decimal RentAmount { get; set; }
    public string? Description { get; set; }

    public string LandlordId { get; set; } = string.Empty;
    public ApplicationUser? Landlord { get; set; }
}