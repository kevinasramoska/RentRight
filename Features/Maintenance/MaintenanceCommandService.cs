using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed class MaintenanceCommandService(
    IMaintenanceStore store,
    ICurrentUser currentUser,
    IClock clock) : IMaintenanceCommandService
{
    private const string AdministratorRole = "Admin";
    private const string LandlordRole = "Landlord";
    private const string TenantRole = "Tenant";
    private const int MinimumDescriptionLength = 10;
    private const int MaximumDescriptionLength = 2000;

    public async Task<CreateMaintenanceRequestOutcome> CreateAsync(
        CreateMaintenanceRequestCommand command,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return CreateMaintenanceRequestOutcome.Unauthenticated();
        }

        if (!currentUser.IsInRole(TenantRole))
        {
            return CreateMaintenanceRequestOutcome.Forbidden();
        }

        var errors = ValidateCreate(command, out var description);
        if (errors.Count > 0)
        {
            return CreateMaintenanceRequestOutcome.Validation(errors);
        }

        var tenancy = await store.GetTenancyAsync(
            command.TenancyId!.Value,
            cancellationToken);
        if (tenancy is null)
        {
            return CreateMaintenanceRequestOutcome.NotFound();
        }

        if (tenancy.TenantId != userId)
        {
            return CreateMaintenanceRequestOutcome.ConcealedNotFound();
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (!IsActiveOn(tenancy, today))
        {
            return CreateMaintenanceRequestOutcome.Validation(
                [new MaintenanceValidationError(
                    nameof(command.TenancyId),
                    "Select a tenancy that is currently active for your account.")]);
        }

        var now = clock.UtcNow;
        try
        {
            var requestId = await store.CreateAsync(
                new NewMaintenanceRequest(
                    tenancy.Id,
                    description,
                    MaintenanceRequestStatus.Submitted,
                    now,
                    now),
                cancellationToken);
            return CreateMaintenanceRequestOutcome.Success(requestId);
        }
        catch (DbUpdateException)
        {
            return CreateMaintenanceRequestOutcome.Conflict();
        }
    }

    public async Task<UpdateMaintenanceRequestStatusOutcome> UpdateStatusAsync(
        UpdateMaintenanceRequestStatusCommand command,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return UpdateMaintenanceRequestStatusOutcome.Unauthenticated();
        }

        var isAdministrator = currentUser.IsInRole(AdministratorRole);
        var isLandlord = currentUser.IsInRole(LandlordRole);
        if (!isAdministrator && !isLandlord)
        {
            return UpdateMaintenanceRequestStatusOutcome.Forbidden();
        }

        if (command.RequestId != command.SubmittedRequestId)
        {
            return UpdateMaintenanceRequestStatusOutcome.ConcealedNotFound();
        }

        var request = await store.GetRequestAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return UpdateMaintenanceRequestStatusOutcome.NotFound();
        }

        if (!isAdministrator && request.LandlordId != userId)
        {
            return UpdateMaintenanceRequestStatusOutcome.ConcealedNotFound();
        }

        if (!command.NewStatus.HasValue ||
            !Enum.IsDefined(command.NewStatus.Value) ||
            !request.Status.CanTransitionTo(command.NewStatus.Value))
        {
            return UpdateMaintenanceRequestStatusOutcome.Validation(
                [new MaintenanceValidationError(
                    nameof(command.NewStatus),
                    "The selected status transition is not allowed.")]);
        }

        try
        {
            var updated = await store.TryUpdateStatusAsync(
                request.Id,
                request.Status,
                command.NewStatus.Value,
                clock.UtcNow,
                cancellationToken);
            return updated
                ? UpdateMaintenanceRequestStatusOutcome.Success(request.Id)
                : UpdateMaintenanceRequestStatusOutcome.Conflict();
        }
        catch (DbUpdateException)
        {
            return UpdateMaintenanceRequestStatusOutcome.Conflict();
        }
    }

    private static List<MaintenanceValidationError> ValidateCreate(
        CreateMaintenanceRequestCommand command,
        out string description)
    {
        var errors = new List<MaintenanceValidationError>();
        description = command.Description?.Trim() ?? string.Empty;

        if (!command.TenancyId.HasValue || command.TenancyId.Value <= 0)
        {
            errors.Add(new MaintenanceValidationError(
                nameof(command.TenancyId),
                "Select an active tenancy."));
        }

        if (description.Length < MinimumDescriptionLength)
        {
            errors.Add(new MaintenanceValidationError(
                nameof(command.Description),
                "Description must contain at least 10 non-whitespace characters."));
        }
        else if (description.Length > MaximumDescriptionLength)
        {
            errors.Add(new MaintenanceValidationError(
                nameof(command.Description),
                "Description cannot exceed 2000 characters."));
        }

        return errors;
    }

    private static bool IsActiveOn(MaintenanceTenancyStoreData tenancy, DateOnly date) =>
        tenancy.Status == TenancyStatus.Active &&
        tenancy.StartDate <= date &&
        (tenancy.EndDate is null || tenancy.EndDate >= date);

    private string? GetAuthenticatedUserId() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : null;
}
