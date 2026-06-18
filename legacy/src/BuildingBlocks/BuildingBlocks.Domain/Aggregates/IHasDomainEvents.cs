using BuildingBlocks.Domain.Events;

namespace BuildingBlocks.Domain.Aggregates;

public interface IHasDomainEvents
{
    IReadOnlyList<DomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
