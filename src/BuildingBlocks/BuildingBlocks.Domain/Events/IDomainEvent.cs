namespace BuildingBlocks.Domain.Events;

/// <summary>
/// A business event that already happened inside a module boundary, described in past tense.
/// Dispatched in-process after the state change is persisted. Kept free of any messaging
/// library so the domain has no infrastructure dependency; the application/infrastructure
/// layer bridges these to the in-process dispatcher.
/// </summary>
public interface IDomainEvent
{
    public Guid EventId { get; }
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>Convenience base that supplies a unique id and an occurrence timestamp.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
