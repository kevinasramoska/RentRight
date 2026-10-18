using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Authorization;
using RentRight.Features.Properties;

namespace RentRight.Controllers;

public sealed class LocationRefreshInput
{
    [Required, StringLength(24)]
    public string? ExpectedVersion { get; init; }
}

[Authorize(Policy = RentalPolicies.Manager)]
public sealed class PropertyLocationsController(IPropertyLocationService locations) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(int id, LocationRefreshInput input, CancellationToken cancellationToken)
    {
        // Ownership is checked before version validation in the service, even on a bad form.
        var result = await locations.RefreshAsync(id, input.ExpectedVersion, cancellationToken);
        if (result == LocationRefreshStatus.Unauthenticated) return Challenge();
        if (result is LocationRefreshStatus.NotFound or LocationRefreshStatus.Forbidden) return NotFound();
        if (result == LocationRefreshStatus.InvalidVersion) return BadRequest("Reload the property and supply its version.");
        TempData["LocationMessage"] = result switch
        {
            LocationRefreshStatus.Updated => "Approximate location updated. Please check it on the map.",
            LocationRefreshStatus.Conflict => "The property changed. Reload it before trying again.",
            _ => "Location is unavailable or the lookup is disabled. Your property is saved; please try again later."
        };
        return RedirectToAction("Details", "Properties", new { id });
    }
}
