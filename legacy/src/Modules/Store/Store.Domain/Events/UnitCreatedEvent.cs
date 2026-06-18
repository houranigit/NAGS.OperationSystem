using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.Unit;

namespace Store.Domain.Events;

public sealed class UnitCreatedEvent(UnitId unitId) : DomainEvent
{
    public UnitId UnitId { get; } = unitId;
}
