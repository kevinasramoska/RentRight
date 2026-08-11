using RentRight.Application.Abstractions;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;

namespace RentRight.Features.Tenancies;

public sealed class TenancyService(
    ITenancyStore tenancyStore,
    ICurrentUser currentUser,
    IClock clock) : ITenancyService
{
    private const string AdministratorRole = "Admin";
    private const int MaximumAdminTenancies = 250;
    private const int MaximumSelectionOptions = 200;

    public async Task<ListTenanciesOutcome> ListForAdministrationAsync(
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetListAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var tenancies = await tenancyStore.ListForAdministrationAsync(
            MaximumAdminTenancies,
            cancellationToken);
        return ListTenanciesOutcome.Success(tenancies);
    }

    public async Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetFormAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var properties = await tenancyStore.ListAvailablePropertiesAsync(
            MaximumSelectionOptions,
            cancellationToken);
        var tenants = await tenancyStore.ListTenantAccountsAsync(
            MaximumSelectionOptions,
            cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow);

        return GetTenancyCreateFormOutcome.Success(
            new TenancyCreateForm(today, properties, tenants));
    }

    public async Task<CreateTenancyOutcome> CreateAsync(
        CreateTenancyCommand command,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetCreateAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var validationErrors = Validate(command, today);
        if (validationErrors.Count > 0)
        {
            return CreateTenancyOutcome.ValidationFailed(validationErrors);
        }

        var propertyId = command.PropertyId!.Value;
        if (!await tenancyStore.PropertyExistsAsync(propertyId, cancellationToken))
        {
            return CreateTenancyOutcome.PropertyNotFound();
        }

        if (!await tenancyStore.TenantAccountExistsAsync(
                command.TenantId,
                cancellationToken))
        {
            return CreateTenancyOutcome.TenantNotFound();
        }

        if (await tenancyStore.HasActiveTenancyAsync(propertyId, cancellationToken))
        {
            return CreateTenancyOutcome.Conflict();
        }

        var persistenceResult = await tenancyStore.CreateAsync(
            new CreateTenancyRecord(
                propertyId,
                command.TenantId,
                command.StartDate,
                command.EndDate),
            cancellationToken);

        return persistenceResult == CreateTenancyPersistenceResult.Success
            ? CreateTenancyOutcome.Success()
            : CreateTenancyOutcome.Conflict();
    }

    public async Task<EndTenancyOutcome> EndAsync(
        int tenancyId,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = GetEndAuthorizationFailure();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var tenancy = await tenancyStore.GetForEndAsync(tenancyId, cancellationToken);
        if (tenancy is null)
        {
            return EndTenancyOutcome.NotFound();
        }

        if (tenancy.Status != TenancyStatus.Active)
        {
            return EndTenancyOutcome.Success(changed: false);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var endDate = tenancy.StartDate > today ? tenancy.StartDate : today;
        var persistenceResult = await tenancyStore.SaveEndAsync(
            tenancy.Id,
            endDate,
            cancellationToken);

        return persistenceResult == EndTenancyPersistenceResult.Success
            ? EndTenancyOutcome.Success(changed: true)
            : EndTenancyOutcome.Conflict();
    }

    private static IReadOnlyList<TenancyValidationError> Validate(
        CreateTenancyCommand command,
        DateOnly today)
    {
        var errors = new List<TenancyValidationError>();

        if (command.PropertyId is null or <= 0)
        {
            errors.Add(new(nameof(command.PropertyId), "Select a valid property."));
        }

        if (string.IsNullOrWhiteSpace(command.TenantId))
        {
            errors.Add(new(nameof(command.TenantId), "Select a valid tenant account."));
        }

        if (command.StartDate == default)
        {
            errors.Add(new(nameof(command.StartDate), "Enter a valid start date."));
        }

        if (command.EndDate is not null && command.EndDate < command.StartDate)
        {
            errors.Add(new(
                nameof(command.EndDate),
                "End date cannot be before the start date."));
        }

        if (command.EndDate is not null && command.EndDate < today)
        {
            errors.Add(new(
                nameof(command.EndDate),
                "An active tenancy cannot have an end date in the past."));
        }

        return errors;
    }

    private ListTenanciesOutcome? GetListAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return ListTenanciesOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : ListTenanciesOutcome.Forbidden();
    }

    private GetTenancyCreateFormOutcome? GetFormAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return GetTenancyCreateFormOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : GetTenancyCreateFormOutcome.Forbidden();
    }

    private CreateTenancyOutcome? GetCreateAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return CreateTenancyOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : CreateTenancyOutcome.Forbidden();
    }

    private EndTenancyOutcome? GetEndAuthorizationFailure()
    {
        if (!currentUser.IsAuthenticated)
        {
            return EndTenancyOutcome.Unauthenticated();
        }

        return currentUser.IsInRole(AdministratorRole)
            ? null
            : EndTenancyOutcome.Forbidden();
    }
}
