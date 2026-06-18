using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Station;

namespace Core.Domain.Events;

public sealed class StationActivatedEvent(StationId stationId) : DomainEvent
{
    public StationId StationId { get; } = stationId;
}
