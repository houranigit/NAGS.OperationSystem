namespace BuildingBlocks.Contracts.Messaging;

/// <summary>
/// A stable, cross-module event. Integration events are written to the originating module's
/// outbox in the same transaction as the state change and dispatched after commit. They must
/// carry ids and business facts, never internal aggregate graphs.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Stable id used for inbox idempotency on the consuming side.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Handles a received integration event. Implementations must be idempotent (guard with the
/// module inbox) because the outbox dispatcher may deliver the same event more than once.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    public Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
