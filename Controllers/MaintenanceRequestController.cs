using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentRight.Authorization;
using RentRight.Features.Maintenance;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;
using RentRight.ViewModels;

namespace RentRight.Controllers;

[Authorize(Policy = RentalPolicies.Participant)]
public class MaintenanceRequestController(
    IMaintenanceQueryService queryService,
    IMaintenanceCommandService commandService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var outcome = await queryService.ListAsync(
            new MaintenanceRequestsQuery(),
            cancellationToken);

        return outcome.Status switch
        {
            MaintenanceOutcomeStatus.Success => View(ToIndexViewModel(outcome.List!)),
            MaintenanceOutcomeStatus.Unauthenticated => Challenge(),
            MaintenanceOutcomeStatus.Forbidden => Forbid(),
            MaintenanceOutcomeStatus.Unavailable => StatusCode(503),
            _ => throw new InvalidOperationException("Unknown maintenance list outcome.")
        };
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var outcome = await queryService.GetDetailAsync(id, cancellationToken);

        return outcome.Status switch
        {
            MaintenanceOutcomeStatus.Success => View(ToDetailsViewModel(outcome.Detail!)),
            MaintenanceOutcomeStatus.Unauthenticated => Challenge(),
            MaintenanceOutcomeStatus.Forbidden => Forbid(),
            MaintenanceOutcomeStatus.Unavailable => StatusCode(503),
            MaintenanceOutcomeStatus.NotFound or
                MaintenanceOutcomeStatus.ConcealedNotFound => NotFound(),
            _ => throw new InvalidOperationException("Unknown maintenance detail outcome.")
        };
    }

    [Authorize(Policy = RentalPolicies.Tenant)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var outcome = await queryService.GetCreateFormAsync(cancellationToken);
        return MapCreateFormOutcome(outcome, new MaintenanceRequestCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = RentalPolicies.Tenant)]
    public async Task<IActionResult> Create(
        MaintenanceRequestCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var outcome = await commandService.CreateAsync(
            new CreateMaintenanceRequestCommand(model.TenancyId, model.Description),
            cancellationToken);

        switch (outcome.Status)
        {
            case MaintenanceOutcomeStatus.Success:
                TempData["Success"] = "Maintenance request submitted.";
                return RedirectToAction(nameof(Details), new { id = outcome.RequestId });
            case MaintenanceOutcomeStatus.Unauthenticated:
                return Challenge();
            case MaintenanceOutcomeStatus.Forbidden:
                return Forbid();
            case MaintenanceOutcomeStatus.Unavailable:
                return StatusCode(503);
            case MaintenanceOutcomeStatus.NotFound:
            case MaintenanceOutcomeStatus.ConcealedNotFound:
                return NotFound();
            case MaintenanceOutcomeStatus.Conflict:
                return Conflict();
            case MaintenanceOutcomeStatus.Validation:
                AddValidationErrors(outcome.Errors);
                var formOutcome = await queryService.GetCreateFormAsync(cancellationToken);
                return MapCreateFormOutcome(formOutcome, model);
            default:
                throw new InvalidOperationException("Unknown maintenance create outcome.");
        }
    }

    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> UpdateStatus(int id, CancellationToken cancellationToken)
    {
        var outcome = await queryService.GetStatusUpdateAsync(id, cancellationToken);
        return MapStatusFormOutcome(outcome);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = RentalPolicies.Manager)]
    public async Task<IActionResult> UpdateStatus(
        int id,
        MaintenanceRequestStatusUpdateInputModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return MapStatusFormOutcome(await queryService.GetStatusUpdateAsync(id, cancellationToken), model.NewStatus);
        var outcome = await commandService.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(id, model.Id, model.NewStatus, model.ExpectedRevision, model.Priority, model.Note),
            cancellationToken);

        switch (outcome.Status)
        {
            case MaintenanceOutcomeStatus.Success:
                TempData["Success"] = "Maintenance request status updated.";
                return RedirectToAction(nameof(Details), new { id = outcome.RequestId });
            case MaintenanceOutcomeStatus.Unauthenticated:
                return Challenge();
            case MaintenanceOutcomeStatus.Forbidden:
                return Forbid();
            case MaintenanceOutcomeStatus.Unavailable:
                return StatusCode(503);
            case MaintenanceOutcomeStatus.NotFound:
            case MaintenanceOutcomeStatus.ConcealedNotFound:
                return NotFound();
            case MaintenanceOutcomeStatus.Conflict:
                return Conflict("The request changed. Reload it before trying again.");
            case MaintenanceOutcomeStatus.Validation:
                AddValidationErrors(outcome.Errors);
                var formOutcome = await queryService.GetStatusUpdateAsync(
                    id,
                    cancellationToken);
                return MapStatusFormOutcome(formOutcome, model.NewStatus);
            default:
                throw new InvalidOperationException("Unknown maintenance status outcome.");
        }
    }

    private IActionResult MapCreateFormOutcome(
        MaintenanceCreateFormOutcome outcome,
        MaintenanceRequestCreateViewModel model)
    {
        switch (outcome.Status)
        {
            case MaintenanceOutcomeStatus.Success:
                model.AvailableTenancies = outcome.Form!.AvailableTenancies
                    .Select(tenancy => new SelectListItem
                    {
                        Value = tenancy.TenancyId.ToString(),
                        Text = tenancy.PropertyTitle + " — " + tenancy.PropertyAddress
                    })
                    .ToArray();
                return View(model);
            case MaintenanceOutcomeStatus.Unauthenticated:
                return Challenge();
            case MaintenanceOutcomeStatus.Forbidden:
                return Forbid();
            case MaintenanceOutcomeStatus.Unavailable:
                return StatusCode(503);
            default:
                throw new InvalidOperationException("Unknown maintenance create-form outcome.");
        }
    }

    private IActionResult MapStatusFormOutcome(
        MaintenanceStatusUpdateOutcome outcome,
        MaintenanceRequestStatus? selectedStatus = null)
    {
        return outcome.Status switch
        {
            MaintenanceOutcomeStatus.Success => View(ToStatusUpdateViewModel(
                outcome.Update!,
                selectedStatus)),
            MaintenanceOutcomeStatus.Unauthenticated => Challenge(),
            MaintenanceOutcomeStatus.Forbidden => Forbid(),
            MaintenanceOutcomeStatus.Unavailable => StatusCode(503),
            MaintenanceOutcomeStatus.NotFound or
                MaintenanceOutcomeStatus.ConcealedNotFound => NotFound(),
            _ => throw new InvalidOperationException("Unknown maintenance status-form outcome.")
        };
    }

    private void AddValidationErrors(IReadOnlyList<MaintenanceValidationError>? errors)
    {
        foreach (var error in errors ?? [])
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }

    private static MaintenanceRequestIndexViewModel ToIndexViewModel(
        MaintenanceRequestList list) =>
        new()
        {
            Heading = list.Audience switch
            {
                MaintenanceListAudience.Administrator => "All Maintenance Requests",
                MaintenanceListAudience.Landlord => "Maintenance Requests for My Properties",
                _ => "My Maintenance Requests"
            },
            CanCreate = list.CanCreate,
            Requests = list.Requests
                .Select(request => new MaintenanceRequestRowViewModel
                {
                    Id = request.Id,
                    PropertyTitle = request.PropertyTitle,
                    TenantName = request.TenantName,
                    Description = request.Description,
                    DateSubmitted = request.DateSubmitted,
                    Status = request.Status,
                    CanUpdateStatus = request.CanUpdateStatus
                })
                .ToArray()
        };

    private static MaintenanceRequestDetailsViewModel ToDetailsViewModel(
        MaintenanceRequestDetail detail) =>
        new()
        {
            History = detail.History,
            Priority = detail.Priority,
            Revision = detail.Revision,
            Id = detail.Id,
            PropertyTitle = detail.PropertyTitle,
            PropertyAddress = detail.PropertyAddress,
            TenantName = detail.TenantName,
            Description = detail.Description,
            DateSubmitted = detail.DateSubmitted,
            LastUpdatedUtc = detail.LastUpdatedUtc,
            Status = detail.Status,
            CanUpdateStatus = detail.CanUpdateStatus
        };

    private static MaintenanceRequestStatusUpdateViewModel ToStatusUpdateViewModel(
        MaintenanceStatusUpdate update,
        MaintenanceRequestStatus? selectedStatus) =>
        new()
        {
            Id = update.RequestId,
            PropertyTitle = update.PropertyTitle,
            TenantName = update.TenantName,
            CurrentStatus = update.CurrentStatus,
            ExpectedRevision = update.Revision,
            Priority = update.Priority,
            NewStatus = selectedStatus,
            AllowedStatuses = update.AllowedStatuses
                .Select(status => new SelectListItem
                {
                    Value = status.ToString(),
                    Text = status == MaintenanceRequestStatus.InProgress
                        ? "In Progress"
                        : status.ToString()
                })
                .ToArray()
        };
}
