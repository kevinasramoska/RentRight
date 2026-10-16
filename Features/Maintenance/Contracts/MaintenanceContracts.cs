using RentRight.Models;

namespace RentRight.Features.Maintenance.Contracts;

public enum MaintenanceOutcomeStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    ConcealedNotFound,
    Validation,
    Conflict
}

public sealed record MaintenanceValidationError(string Field, string Message);

public sealed record MaintenanceRequestsQuery(int Page = 1, int PageSize = 50)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;
    public const int MaximumPage = 10_000;
}

public enum MaintenanceListAudience
{
    Tenant,
    Landlord,
    Administrator
}

public sealed record MaintenanceRequestRow(
    int Id,
    string PropertyTitle,
    string TenantName,
    string Description,
    DateTime DateSubmitted,
    MaintenanceRequestStatus Status,
    bool CanUpdateStatus);

public sealed record MaintenanceRequestList(
    MaintenanceListAudience Audience,
    bool CanCreate,
    IReadOnlyList<MaintenanceRequestRow> Requests,
    int Page,
    int PageSize);

public sealed record MaintenanceRequestDetail(
    int Id,
    string PropertyTitle,
    string PropertyAddress,
    string TenantName,
    string Description,
    DateTime DateSubmitted,
    DateTime LastUpdatedUtc,
    MaintenanceRequestStatus Status,
    bool CanUpdateStatus)
{
    public IReadOnlyList<MaintenanceHistoryItem> History { get; init; } = [];
}


public sealed record MaintenanceTenancyOption(
    int TenancyId,
    string PropertyTitle,
    string PropertyAddress);

public sealed record MaintenanceCreateForm(
    IReadOnlyList<MaintenanceTenancyOption> AvailableTenancies);

public sealed record CreateMaintenanceRequestCommand(
    int? TenancyId,
    string? Description);

public sealed record MaintenanceStatusUpdate(
    int RequestId,
    string PropertyTitle,
    string TenantName,
    MaintenanceRequestStatus CurrentStatus,
    IReadOnlyList<MaintenanceRequestStatus> AllowedStatuses);

public sealed record UpdateMaintenanceRequestStatusCommand(
    int RequestId,
    int SubmittedRequestId,
    MaintenanceRequestStatus? NewStatus);

public sealed record MaintenanceListOutcome(
    MaintenanceOutcomeStatus Status,
    MaintenanceRequestList? List = null)
{
    public static MaintenanceListOutcome Success(MaintenanceRequestList list) =>
        new(MaintenanceOutcomeStatus.Success, list);

    public static MaintenanceListOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static MaintenanceListOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);
}

public sealed record MaintenanceDetailOutcome(
    MaintenanceOutcomeStatus Status,
    MaintenanceRequestDetail? Detail = null)
{
    public static MaintenanceDetailOutcome Success(MaintenanceRequestDetail detail) =>
        new(MaintenanceOutcomeStatus.Success, detail);

    public static MaintenanceDetailOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static MaintenanceDetailOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);

    public static MaintenanceDetailOutcome NotFound() =>
        new(MaintenanceOutcomeStatus.NotFound);

    public static MaintenanceDetailOutcome ConcealedNotFound() =>
        new(MaintenanceOutcomeStatus.ConcealedNotFound);
}

public sealed record MaintenanceCreateFormOutcome(
    MaintenanceOutcomeStatus Status,
    MaintenanceCreateForm? Form = null)
{
    public static MaintenanceCreateFormOutcome Success(MaintenanceCreateForm form) =>
        new(MaintenanceOutcomeStatus.Success, form);

    public static MaintenanceCreateFormOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static MaintenanceCreateFormOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);
}

public sealed record MaintenanceStatusUpdateOutcome(
    MaintenanceOutcomeStatus Status,
    MaintenanceStatusUpdate? Update = null)
{
    public static MaintenanceStatusUpdateOutcome Success(MaintenanceStatusUpdate update) =>
        new(MaintenanceOutcomeStatus.Success, update);

    public static MaintenanceStatusUpdateOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static MaintenanceStatusUpdateOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);

    public static MaintenanceStatusUpdateOutcome NotFound() =>
        new(MaintenanceOutcomeStatus.NotFound);

    public static MaintenanceStatusUpdateOutcome ConcealedNotFound() =>
        new(MaintenanceOutcomeStatus.ConcealedNotFound);
}

public sealed record CreateMaintenanceRequestOutcome(
    MaintenanceOutcomeStatus Status,
    int? RequestId = null,
    IReadOnlyList<MaintenanceValidationError>? Errors = null)
{
    public static CreateMaintenanceRequestOutcome Success(int requestId) =>
        new(MaintenanceOutcomeStatus.Success, requestId);

    public static CreateMaintenanceRequestOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static CreateMaintenanceRequestOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);

    public static CreateMaintenanceRequestOutcome NotFound() =>
        new(MaintenanceOutcomeStatus.NotFound);

    public static CreateMaintenanceRequestOutcome ConcealedNotFound() =>
        new(MaintenanceOutcomeStatus.ConcealedNotFound);

    public static CreateMaintenanceRequestOutcome Validation(
        IReadOnlyList<MaintenanceValidationError> errors) =>
        new(MaintenanceOutcomeStatus.Validation, Errors: errors);

    public static CreateMaintenanceRequestOutcome Conflict() =>
        new(MaintenanceOutcomeStatus.Conflict);
}

public sealed record UpdateMaintenanceRequestStatusOutcome(
    MaintenanceOutcomeStatus Status,
    int? RequestId = null,
    IReadOnlyList<MaintenanceValidationError>? Errors = null)
{
    public static UpdateMaintenanceRequestStatusOutcome Success(int requestId) =>
        new(MaintenanceOutcomeStatus.Success, requestId);

    public static UpdateMaintenanceRequestStatusOutcome Unauthenticated() =>
        new(MaintenanceOutcomeStatus.Unauthenticated);

    public static UpdateMaintenanceRequestStatusOutcome Forbidden() =>
        new(MaintenanceOutcomeStatus.Forbidden);

    public static UpdateMaintenanceRequestStatusOutcome NotFound() =>
        new(MaintenanceOutcomeStatus.NotFound);

    public static UpdateMaintenanceRequestStatusOutcome ConcealedNotFound() =>
        new(MaintenanceOutcomeStatus.ConcealedNotFound);

    public static UpdateMaintenanceRequestStatusOutcome Validation(
        IReadOnlyList<MaintenanceValidationError> errors) =>
        new(MaintenanceOutcomeStatus.Validation, Errors: errors);

    public static UpdateMaintenanceRequestStatusOutcome Conflict() =>
        new(MaintenanceOutcomeStatus.Conflict);
}
