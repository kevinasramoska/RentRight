using RentRight.Models;

namespace RentRight.Features.Maintenance;

public sealed record MaintenanceListAccess(
    string UserId,
    bool IncludeAll,
    bool IncludeLandlordOwned,
    bool IncludeTenantOwned,
    DateOnly Today);

public sealed record MaintenanceListStoreRow(
    int Id,
    string PropertyTitle,
    string TenantName,
    string Description,
    DateTime DateSubmitted,
    MaintenanceRequestStatus Status,
    string LandlordId,
    string? TenantId);

public sealed record MaintenanceRequestStoreData(
    int Id,
    string PropertyTitle,
    string PropertyAddress,
    string TenantName,
    string Description,
    DateTime DateSubmitted,
    DateTime LastUpdatedUtc,
    MaintenanceRequestStatus Status,
    string? TenantId,
    string LandlordId,
    int TenancyId,
    MaintenancePriority Priority = MaintenancePriority.Normal,
    long Revision = 0, byte[]? RowVersion = null);

public sealed record MaintenanceTenancyStoreData(
    int Id,
    string? TenantId,
    TenancyStatus Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PropertyTitle,
    string PropertyAddress);

public sealed record NewMaintenanceRequest(
    int TenancyId,
    string Description,
    MaintenanceRequestStatus Status,
    DateTime DateSubmitted,
    DateTime LastUpdatedUtc,
    string TenantId);

public sealed record MaintenanceMutation(long ExpectedRevision, MaintenancePriority Priority, string ActorUserId, bool IsAdmin, string? Note, byte[]? ExpectedRowVersion = null);

public interface IMaintenanceStore
{
    Task<IReadOnlyList<MaintenanceHistoryItem>> GetHistoryAsync(int requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaintenanceListStoreRow>> ListAsync(
        MaintenanceListAccess access,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MaintenanceRequestStoreData?> GetRequestAsync(
        int requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaintenanceTenancyStoreData>> ListActiveTenanciesAsync(
        string tenantId,
        DateOnly date,
        CancellationToken cancellationToken);

    Task<MaintenanceTenancyStoreData?> GetTenancyAsync(
        int tenancyId,
        CancellationToken cancellationToken);

    Task<int> CreateAsync(
        NewMaintenanceRequest request,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateStatusAsync(
        int requestId,
        MaintenanceRequestStatus expectedStatus,
        MaintenanceRequestStatus newStatus,
        DateTime lastUpdatedUtc,
        CancellationToken cancellationToken,
        MaintenanceMutation mutation);
}
