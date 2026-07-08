using BuildingBlocks.Domain.Entities;
using Operations.Domain.Enumerations;

namespace Operations.Domain.Flights;

/// <summary>
/// One portal-visible event on a flight's timeline/history. Written by application handlers in the
/// same transaction as the state change; append-only.
/// </summary>
public sealed class FlightTimelineEntry : Entity<Guid>
{
    private FlightTimelineEntry() { }

    public FlightTimelineEntry(
        Guid flightId,
        FlightTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string? actorName,
        string? details = null)
    {
        Id = Guid.NewGuid();
        FlightId = flightId;
        EventType = eventType;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorName = actorName;
        Details = details;
    }

    public Guid FlightId { get; private set; }
    public FlightTimelineEventType EventType { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid ActorUserId { get; private set; }

    /// <summary>Display name of the actor resolved at write time (staff full name when available).</summary>
    public string? ActorName { get; private set; }

    public string? Details { get; private set; }
}
