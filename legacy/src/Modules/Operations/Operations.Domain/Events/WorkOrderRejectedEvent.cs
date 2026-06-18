using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Events;

public sealed class WorkOrderRejectedEvent(WorkOrderId workOrderId) : DomainEvent
{
    public WorkOrderId WorkOrderId { get; } = workOrderId;
}
