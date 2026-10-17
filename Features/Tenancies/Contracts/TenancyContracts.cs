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
    TenancyStatus Status,
    string? Version = null);

public sealed record TenancySelectionOption(string Value, string Label);

public sealed record TenancyCreateForm(
    DateOnly DefaultStartDate,
    IReadOnlyList<TenancySelectionOption> AvailableProperties);

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
    Conflict
}

public sealed record CreateTenancyOutcome(
    CreateTenancyStatus Status,
    IReadOnlyList<TenancyValidationError>? Errors = null,
    InvitationReceipt? Invitation = null)
{
    public static CreateTenancyOutcome Success(InvitationReceipt invitation) =>
        new(CreateTenancyStatus.Success, Invitation: invitation);

    public static CreateTenancyOutcome Unauthenticated() =>
        new(CreateTenancyStatus.Unauthenticated);

    public static CreateTenancyOutcome Forbidden() =>
        new(CreateTenancyStatus.Forbidden);

    public static CreateTenancyOutcome ValidationFailed(
        IReadOnlyList<TenancyValidationError> errors) =>
        new(CreateTenancyStatus.ValidationFailed, errors);

    public static CreateTenancyOutcome PropertyNotFound() =>
        new(CreateTenancyStatus.PropertyNotFound);

    public static CreateTenancyOutcome Conflict() =>
        new(CreateTenancyStatus.Conflict);
}

public enum EndTenancyStatus
{
    InvalidVersion,
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

// Raw token appears only in the issuance response and the acceptance request.
public sealed record InvitationReceipt(string Token, DateTime ExpiresUtc);
public sealed record AcceptTenancyCommand(string Token);
public enum AcceptTenancyStatus { Success, Unauthenticated, Forbidden, NotFound, Conflict }
public sealed record AcceptTenancyOutcome(AcceptTenancyStatus Status);
public sealed record GetTenancyOutcome(TenancyQueryStatus Status, TenancyAdminRow? Tenancy = null);
public sealed record TenancyPage(IReadOnlyList<TenancyAdminRow> Items, int TotalCount);

public sealed record ListTenanciesPageOutcome(TenancyQueryStatus Status, TenancyPage? Page = null);
