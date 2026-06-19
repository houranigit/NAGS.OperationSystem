using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Events;

namespace BuildingBlocks.Domain.Aggregates;

/// <summary>
/// Base class for aggregate roots: the consistency boundary that records domain events.
/// Events are collected during a transaction and dispatched after it is persisted.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
