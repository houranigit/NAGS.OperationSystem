using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Events;

public sealed class FlightSettledFromWorkOrderEvent(FlightId flightId, WorkOrderSnapshot snapshot) : DomainEvent
{
    public FlightId FlightId { get; } = flightId;
    public WorkOrderSnapshot Snapshot { get; } = snapshot;
}
