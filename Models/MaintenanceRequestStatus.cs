using System.ComponentModel.DataAnnotations;

namespace RentRight.Models;

public enum MaintenanceRequestStatus
{
    Submitted = 1,

    [Display(Name = "In Progress")]
    InProgress = 2,

    Resolved = 3,
    Closed = 4
}

public static class MaintenanceRequestStatusTransitions
{
    private static readonly IReadOnlyDictionary<MaintenanceRequestStatus, MaintenanceRequestStatus[]> AllowedTransitions =
        new Dictionary<MaintenanceRequestStatus, MaintenanceRequestStatus[]>
        {
            [MaintenanceRequestStatus.Submitted] = [MaintenanceRequestStatus.InProgress],
            [MaintenanceRequestStatus.InProgress] = [MaintenanceRequestStatus.Resolved],
            [MaintenanceRequestStatus.Resolved] = [MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Closed],
            [MaintenanceRequestStatus.Closed] = []
        };

    public static bool CanTransitionTo(
        this MaintenanceRequestStatus current,
        MaintenanceRequestStatus next) =>
        AllowedTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public static IReadOnlyList<MaintenanceRequestStatus> GetAllowedTransitions(
        this MaintenanceRequestStatus current) =>
        AllowedTransitions.TryGetValue(current, out var allowed) ? allowed : [];
}
