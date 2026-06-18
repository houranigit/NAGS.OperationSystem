using MediatR;

namespace BuildingBlocks.Contracts.IntegrationEvents;

/// <summary>
/// Base for all integration events. Uses MediatR INotification so the in-process
/// event bus (IPublisher) can dispatch to IIntegrationEventHandler subscribers.
/// Properties must use primitives only — no domain objects or value objects.
/// </summary>
public abstract record IntegrationEvent : INotification
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
