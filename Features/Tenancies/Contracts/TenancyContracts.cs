using RentRight.Models;

namespace RentRight.Features.Tenancies.Contracts;

public sealed record TenancyAdminRow(
    int Id,
    string PropertyTitle,
    string PropertyAddress,
    string TenantName,
    string TenantEmail,
    DateOnly StartDate,
    DateOnly? EndDate,
    TenancyStatus Status);

public sealed record TenancySelectionOption(string Value, string Label);

public sealed record TenancyCreateForm(
    DateOnly DefaultStartDate,
    IReadOnlyList<TenancySelectionOption> AvailableProperties,
    IReadOnlyList<TenancySelectionOption> AvailableTenants);

public sealed record CreateTenancyCommand(
    int? PropertyId,
    string TenantId,
    DateOnly StartDate,
    DateOnly? EndDate);

public sealed record TenancyValidationError(string Field, string Message);

public enum TenancyQueryStatus
{
    Success,
    Unauthenticated,
    Forbidden
}

public sealed record ListTenanciesOutcome(
    TenancyQueryStatus Status,
    IReadOnlyList<TenancyAdminRow>? Tenancies = null)
{
    public static ListTenanciesOutcome Success(IReadOnlyList<TenancyAdminRow> tenancies) =>
        new(TenancyQueryStatus.Success, tenancies);

    public static ListTenanciesOutcome Unauthenticated() =>
        new(TenancyQueryStatus.Unauthenticated);

    public static ListTenanciesOutcome Forbidden() =>
        new(TenancyQueryStatus.Forbidden);
}

public sealed record GetTenancyCreateFormOutcome(
    TenancyQueryStatus Status,
    TenancyCreateForm? Form = null)
{
    public static GetTenancyCreateFormOutcome Success(TenancyCreateForm form) =>
        new(TenancyQueryStatus.Success, form);

    public static GetTenancyCreateFormOutcome Unauthenticated() =>
        new(TenancyQueryStatus.Unauthenticated);

    public static GetTenancyCreateFormOutcome Forbidden() =>
        new(TenancyQueryStatus.Forbidden);
}

public enum CreateTenancyStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    ValidationFailed,
    PropertyNotFound,
    TenantNotFound,
    Conflict
}

public sealed record CreateTenancyOutcome(
    CreateTenancyStatus Status,
    IReadOnlyList<TenancyValidationError>? Errors = null)
{
    public static CreateTenancyOutcome Success() => new(CreateTenancyStatus.Success);

    public static CreateTenancyOutcome Unauthenticated() =>
        new(CreateTenancyStatus.Unauthenticated);

    public static CreateTenancyOutcome Forbidden() =>
        new(CreateTenancyStatus.Forbidden);

    public static CreateTenancyOutcome ValidationFailed(
        IReadOnlyList<TenancyValidationError> errors) =>
        new(CreateTenancyStatus.ValidationFailed, errors);

    public static CreateTenancyOutcome PropertyNotFound() =>
        new(CreateTenancyStatus.PropertyNotFound);

    public static CreateTenancyOutcome TenantNotFound() =>
        new(CreateTenancyStatus.TenantNotFound);

    public static CreateTenancyOutcome Conflict() =>
        new(CreateTenancyStatus.Conflict);
}

public enum EndTenancyStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record EndTenancyOutcome(EndTenancyStatus Status, bool Changed = false)
{
    public static EndTenancyOutcome Success(bool changed) =>
        new(EndTenancyStatus.Success, changed);

    public static EndTenancyOutcome Unauthenticated() =>
        new(EndTenancyStatus.Unauthenticated);

    public static EndTenancyOutcome Forbidden() =>
        new(EndTenancyStatus.Forbidden);

    public static EndTenancyOutcome NotFound() =>
        new(EndTenancyStatus.NotFound);

    public static EndTenancyOutcome Conflict() =>
        new(EndTenancyStatus.Conflict);
}
