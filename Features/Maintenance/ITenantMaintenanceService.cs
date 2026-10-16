namespace RentRight.Features.Maintenance;

public enum TenantMaintenanceStatus { Success, Unauthenticated, Forbidden, NotFound, NoActiveTenancy, Conflict, Invalid, Unavailable }
public sealed record MaintenanceHistoryItem(string Status, DateTime OccurredUtc, bool IsLegacyBaseline);
public sealed record TenantMaintenanceItem(int Id, string PropertyTitle, string PropertyAddress,
    string Description, DateTime SubmittedUtc, DateTime LastUpdatedUtc, string Status);
public sealed record TenantMaintenanceDetail(TenantMaintenanceItem Request, IReadOnlyList<MaintenanceHistoryItem> History);
public sealed record TenantMaintenancePage(IReadOnlyList<TenantMaintenanceItem> Items, int TotalCount);
public sealed record TenantMaintenanceResult<T>(TenantMaintenanceStatus Status, T? Value = default);

public interface ITenantMaintenanceService
{
    Task<TenantMaintenanceResult<int>> CreateAsync(string? description, CancellationToken cancellationToken);
    Task<TenantMaintenanceResult<TenantMaintenancePage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<TenantMaintenanceResult<TenantMaintenanceDetail>> GetAsync(int id, CancellationToken cancellationToken);
}
