using BuildingBlocks.Domain.Entities;
using Operations.Domain.Enumerations;

namespace Operations.Domain.WorkOrders;

/// <summary>
/// One portal-visible event on a work order's timeline/history. Work order status remains deliberately
/// small (Submitted/Approved); returned and superseded decisions live here.
/// </summary>
public sealed class WorkOrderTimelineEntry : Entity<Guid>
{
    private WorkOrderTimelineEntry() { }

    public WorkOrderTimelineEntry(
        Guid workOrderId,
        Guid flightId,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string? actorName,
        string? workOrderNumber = null,
        string? details = null)
    {
        Id = Guid.NewGuid();
        WorkOrderId = workOrderId;
        FlightId = flightId;
        EventType = eventType;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorName = actorName;
        WorkOrderNumber = workOrderNumber;
        Details = details;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid FlightId { get; private set; }
    public WorkOrderTimelineEventType EventType { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? ActorName { get; private set; }
    public string? WorkOrderNumber { get; private set; }
    public string? Details { get; private set; }
}
