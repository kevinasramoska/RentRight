using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentRight.Models;

namespace RentRight.ViewModels;

public class MaintenanceRequestIndexViewModel
{
    public string Heading { get; init; } = "Maintenance Requests";
    public bool CanCreate { get; init; }
    public IReadOnlyList<MaintenanceRequestRowViewModel> Requests { get; init; } = [];
}

public class MaintenanceRequestRowViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public required string TenantName { get; init; }
    public required string Description { get; init; }
    public DateTime DateSubmitted { get; init; }
    public MaintenanceRequestStatus Status { get; init; }
    public bool CanUpdateStatus { get; init; }
}

public class MaintenanceRequestDetailsViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public required string PropertyAddress { get; init; }
    public required string TenantName { get; init; }
    public required string Description { get; init; }
    public DateTime DateSubmitted { get; init; }
    public DateTime LastUpdatedUtc { get; init; }
    public MaintenanceRequestStatus Status { get; init; }
    public bool CanUpdateStatus { get; init; }
}

public class MaintenanceRequestCreateViewModel
{
    [Required(ErrorMessage = "Select an active tenancy.")]
    [Display(Name = "Property")]
    public int? TenancyId { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    public IReadOnlyList<SelectListItem> AvailableTenancies { get; set; } = [];
}

public class MaintenanceRequestStatusUpdateViewModel
{
    public int Id { get; init; }
    public required string PropertyTitle { get; init; }
    public required string TenantName { get; init; }
    public MaintenanceRequestStatus CurrentStatus { get; init; }

    [Display(Name = "New status")]
    public MaintenanceRequestStatus? NewStatus { get; set; }

    public IReadOnlyList<SelectListItem> AllowedStatuses { get; set; } = [];
}

public class MaintenanceRequestStatusUpdateInputModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "New status")]
    public MaintenanceRequestStatus? NewStatus { get; set; }
}
