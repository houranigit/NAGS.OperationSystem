using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Events;

public sealed class WorkOrderAttachedToFlightEvent(FlightId flightId, WorkOrderId workOrderId) : DomainEvent
{
    public FlightId FlightId { get; } = flightId;
    public WorkOrderId WorkOrderId { get; } = workOrderId;
}
