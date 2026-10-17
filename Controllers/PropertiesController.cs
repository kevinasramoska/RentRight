using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRight.Authorization;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;

namespace RentRight.Controllers;

[Authorize]
public class PropertiesController(IPropertyService propertyService) : Controller
{
    // GET: Properties
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        var outcome = await propertyService.ListAsync(
            new PropertyListQuery(),
            cancellationToken);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => View(outcome.Value),
            _ => throw new InvalidOperationException("Unknown property list outcome.")
        };
    }

    // GET: Properties/Details/5
    [AllowAnonymous]
    public async Task<IActionResult> Details(
        int? id,
        CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return NotFound();
        }

        var outcome = await propertyService.GetDetailsAsync(id.Value, cancellationToken);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => View(outcome.Value),
            PropertyOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Unknown property details outcome.")
        };
    }

    // GET: Properties/Create
    [Authorize(Policy = RentalPolicies.Manager)]
    public IActionResult Create() => View(new PropertyCreateInput());

    // POST: Properties/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> Create(
        PropertyCreateInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var outcome = await propertyService.CreateAsync(input, cancellationToken);
        return MapCreateOutcome(outcome, input);
    }

    // GET: Properties/Edit/5
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> Edit(
        int? id,
        CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return NotFound();
        }

        var outcome = await propertyService.GetEditAsync(id.Value, cancellationToken);
        return MapQueryOutcome(outcome);
    }

    // POST: Properties/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> Edit(
        int id,
        PropertyEditInput input,
        CancellationToken cancellationToken = default)
    {
        if (id != input.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            var accessOutcome = await propertyService.GetEditAsync(id, cancellationToken);
            return accessOutcome.Status switch
            {
                PropertyOperationStatus.Success => View(input),
                PropertyOperationStatus.Unauthenticated => Challenge(),
                PropertyOperationStatus.Forbidden => Forbid(),
                PropertyOperationStatus.NotFound => NotFound(),
                _ => throw new InvalidOperationException("Unknown property edit outcome.")
            };
        }

        var outcome = await propertyService.UpdateAsync(id, input, cancellationToken);
        return MapUpdateOutcome(outcome, input);
    }

    // GET: Properties/Delete/5
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> Delete(
        int? id,
        CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return NotFound();
        }

        var outcome = await propertyService.GetDeleteAsync(id.Value, cancellationToken);
        return MapQueryOutcome(outcome);
    }

    // POST: Properties/DeleteConfirmed/5
    [HttpPost]
    [ActionName("DeleteConfirmed")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> DeleteConfirmed(
        int id,
        CancellationToken cancellationToken = default,
        string? expectedVersion = null)
    {
        var outcome = await propertyService.DeleteAsync(id, cancellationToken, expectedVersion);
        return outcome.Status switch
        {
            PropertyOperationStatus.Success => RedirectToAction(nameof(Index)),
            PropertyOperationStatus.Unauthenticated => Challenge(),
            PropertyOperationStatus.Forbidden => Forbid(),
            PropertyOperationStatus.NotFound => NotFound(),
            PropertyOperationStatus.ValidationFailed => BadRequest("Reload the property and supply its version."),
            PropertyOperationStatus.HasHistory => RedirectWithError(
                "A property with tenancy history cannot be deleted."),
            PropertyOperationStatus.Conflict => RedirectWithError(
                "The property could not be deleted because it changed. Reload the page and try again."),
            _ => throw new InvalidOperationException("Unknown property deletion outcome.")
        };
    }

    private IActionResult MapCreateOutcome(
        PropertyMutationOutcome outcome,
        PropertyCreateInput input) =>
        outcome.Status switch
        {
            PropertyOperationStatus.Success => RedirectToAction(nameof(Index)),
            PropertyOperationStatus.Unauthenticated => Challenge(),
            PropertyOperationStatus.Forbidden => Forbid(),
            PropertyOperationStatus.ValidationFailed => ViewWithValidation(input, outcome),
            PropertyOperationStatus.Conflict => ViewWithError(
                input,
                "The property could not be created. Review the values and try again."),
            _ => throw new InvalidOperationException("Unknown property creation outcome.")
        };

    private IActionResult MapUpdateOutcome(
        PropertyMutationOutcome outcome,
        PropertyEditInput input) =>
        outcome.Status switch
        {
            PropertyOperationStatus.Success => RedirectToAction(nameof(Index)),
            PropertyOperationStatus.Unauthenticated => Challenge(),
            PropertyOperationStatus.Forbidden => Forbid(),
            PropertyOperationStatus.NotFound => NotFound(),
            PropertyOperationStatus.ValidationFailed => ViewWithValidation(input, outcome),
            PropertyOperationStatus.Conflict => ViewWithError(
                input,
                "The property could not be updated because it changed. Reload it and try again."),
            _ => throw new InvalidOperationException("Unknown property update outcome.")
        };

    private IActionResult MapQueryOutcome<T>(PropertyQueryOutcome<T> outcome)
        where T : class =>
        outcome.Status switch
        {
            PropertyOperationStatus.Success => View(outcome.Value),
            PropertyOperationStatus.Unauthenticated => Challenge(),
            PropertyOperationStatus.Forbidden => Forbid(),
            PropertyOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Unknown property query outcome.")
        };

    private IActionResult ViewWithValidation<T>(
        T input,
        PropertyMutationOutcome outcome)
    {
        foreach (var error in outcome.ValidationErrors ??
                 new Dictionary<string, string[]>())
        {
            foreach (var message in error.Value)
            {
                ModelState.AddModelError(error.Key, message);
            }
        }

        return View(input);
    }

    private IActionResult ViewWithError<T>(T input, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return View(input);
    }

    private IActionResult RedirectWithError(string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Index));
    }
}
