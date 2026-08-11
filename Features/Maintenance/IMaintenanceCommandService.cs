using RentRight.Features.Maintenance.Contracts;

namespace RentRight.Features.Maintenance;

public interface IMaintenanceCommandService
{
    Task<CreateMaintenanceRequestOutcome> CreateAsync(
        CreateMaintenanceRequestCommand command,
        CancellationToken cancellationToken);

    Task<UpdateMaintenanceRequestStatusOutcome> UpdateStatusAsync(
        UpdateMaintenanceRequestStatusCommand command,
        CancellationToken cancellationToken);
}
