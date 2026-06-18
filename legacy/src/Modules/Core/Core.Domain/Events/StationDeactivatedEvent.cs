using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Station;

namespace Core.Domain.Events;

public sealed class StationDeactivatedEvent(StationId stationId) : DomainEvent
{
    public StationId StationId { get; } = stationId;
}
