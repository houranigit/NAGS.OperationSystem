using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Events;

/// <summary>
/// Raised when a sibling work order is moved from <c>UnderReview</c> to <c>Deleting</c>
/// because a peer on the same flight was approved. The deletion job hard-removes the
/// row once the configured delay elapses; <see cref="WorkOrderRestoredFromDeletionEvent"/>
/// is raised instead if the winning approval is revoked first.
/// </summary>
public sealed class WorkOrderMarkedForDeletionEvent(WorkOrderId workOrderId, DateTimeOffset markedAt) : DomainEvent
{
    public WorkOrderId WorkOrderId { get; } = workOrderId;
    public DateTimeOffset MarkedAt { get; } = markedAt;
}
