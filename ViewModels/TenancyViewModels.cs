using Microsoft.AspNetCore.Mvc.Rendering;
using RentRight.Models;
using System.ComponentModel.DataAnnotations;

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

public class TenancyCreateViewModel
{
    [Required(ErrorMessage = "Select a property.")]
    [Display(Name = "Property")]
    public int? PropertyId { get; set; }

    [Required(ErrorMessage = "Select a tenant.")]
    [Display(Name = "Tenant")]
    public string TenantId { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    public IReadOnlyList<SelectListItem> AvailableProperties { get; set; } = [];
    public IReadOnlyList<SelectListItem> AvailableTenants { get; set; } = [];
}
