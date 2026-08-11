using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using RentRight.Models;

namespace RentRight.Controllers;

public class HomeController(IPropertyService propertyService) : Controller
{
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        var outcome = await propertyService.ListLatestAsync(
            new LatestPropertiesQuery(),
            cancellationToken);

        return outcome.Status switch
        {
            PropertyOperationStatus.Success => View(outcome.Value),
            _ => throw new InvalidOperationException("Unknown latest property list outcome.")
        };
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
