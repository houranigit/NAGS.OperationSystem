using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Events;

public sealed class FlightCreatedEvent(FlightId flightId) : DomainEvent
{
    public FlightId FlightId { get; } = flightId;
}
