using BuildingBlocks.Domain.Entities;
using Operations.Domain.Enumerations;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrderTimelineEntry : Entity<Guid>
{
    private WorkOrderTimelineEntry() { }

    public WorkOrderTimelineEntry(
        Guid workOrderId,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string? actorName,
        string? details = null)
    {
        Id = Guid.NewGuid();
        WorkOrderId = workOrderId;
        EventType = eventType;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorName = actorName;
        Details = details;
    }

    public Guid WorkOrderId { get; private set; }
    public WorkOrderTimelineEventType EventType { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? ActorName { get; private set; }
    public string? Details { get; private set; }
}
