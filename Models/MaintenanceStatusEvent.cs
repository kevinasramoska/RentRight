namespace RentRight.Models;

// Append-only facts. Legacy baselines describe the state observed during migration,
// not an inferred transition or an invented actor.
public sealed class MaintenanceStatusEvent
{
    private MaintenanceStatusEvent() { }

    public MaintenanceStatusEvent(MaintenanceRequestStatus status, DateTime occurredUtc, bool isLegacyBaseline = false, int maintenanceRequestId = 0,
        MaintenanceRequestStatus? previousStatus = null, string? actorUserId = null, string? note = null)
    {
        MaintenanceRequestId = maintenanceRequestId;
        Status = status;
        PreviousStatus = previousStatus;
        ActorUserId = actorUserId;
        Note = note;
        OccurredUtc = occurredUtc;
        IsLegacyBaseline = isLegacyBaseline;
    }

    public MaintenanceRequestStatus? PreviousStatus { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? Note { get; private set; }
    public long Id { get; private set; }
    public int MaintenanceRequestId { get; private set; }
    public MaintenanceRequestStatus Status { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public bool IsLegacyBaseline { get; private set; }
}
