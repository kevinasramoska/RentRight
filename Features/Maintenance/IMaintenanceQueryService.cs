using RentRight.Features.Maintenance.Contracts;

namespace RentRight.Features.Maintenance;

public interface IMaintenanceQueryService
{
    Task<MaintenanceListOutcome> ListAsync(
        MaintenanceRequestsQuery query,
        CancellationToken cancellationToken);

    Task<MaintenanceDetailOutcome> GetDetailAsync(
        int requestId,
        CancellationToken cancellationToken);

    Task<MaintenanceCreateFormOutcome> GetCreateFormAsync(
        CancellationToken cancellationToken);

    Task<MaintenanceStatusUpdateOutcome> GetStatusUpdateAsync(
        int requestId,
        CancellationToken cancellationToken);
}
