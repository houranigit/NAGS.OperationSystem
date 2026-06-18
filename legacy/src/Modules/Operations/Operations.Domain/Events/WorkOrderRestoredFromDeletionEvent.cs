using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Events;

/// <summary>
/// Raised when a work order in <c>Deleting</c> is restored to <c>UnderReview</c> because
/// the approval that triggered its deletion was revoked.
/// </summary>
public sealed class WorkOrderRestoredFromDeletionEvent(WorkOrderId workOrderId) : DomainEvent
{
    public WorkOrderId WorkOrderId { get; } = workOrderId;
}
