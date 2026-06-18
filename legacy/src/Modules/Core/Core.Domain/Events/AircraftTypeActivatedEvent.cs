using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.AircraftType;

namespace Core.Domain.Events;

public sealed class AircraftTypeActivatedEvent(AircraftTypeId aircraftTypeId) : DomainEvent
{
    public AircraftTypeId AircraftTypeId { get; } = aircraftTypeId;
}
