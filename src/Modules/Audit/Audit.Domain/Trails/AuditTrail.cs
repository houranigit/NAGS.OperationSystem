namespace Audit.Domain.Trails;

/// <summary>
/// A permanent, append-only record of one business or security change. Created only by the Audit
/// module when it consumes an audit event; never updated or deleted. Field-level before/after
/// values are stored as a JSON document (already redacted of secrets by the producer).
/// </summary>
public sealed class AuditTrail
{
    private AuditTrail() { }

    public Guid Id { get; private set; }

    /// <summary>Id of the originating audit event, used for idempotent persistence.</summary>
    public Guid EventId { get; private set; }

    public DateTimeOffset OccurredOnUtc { get; private set; }

    public Guid? ActorId { get; private set; }
    public string? ActorDisplayName { get; private set; }
    public bool IsSystemActor { get; private set; }

    public string Module { get; private set; } = null!;
    public string RootSubjectType { get; private set; } = null!;
    public Guid? RootSubjectId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid? EntityId { get; private set; }
    public string Action { get; private set; } = null!;

    public string? CorrelationId { get; private set; }

    /// <summary>JSON array of { field, before, after }. Null when there are no field deltas.</summary>
    public string? ChangesJson { get; private set; }

    public string? Metadata { get; private set; }

    public static AuditTrail Create(
        Guid eventId,
        DateTimeOffset occurredOnUtc,
        Guid? actorId,
        string? actorDisplayName,
        bool isSystemActor,
        string module,
        string rootSubjectType,
        Guid? rootSubjectId,
        string entityType,
        Guid? entityId,
        string action,
        string? correlationId,
        string? changesJson,
        string? metadata) => new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            OccurredOnUtc = occurredOnUtc,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            IsSystemActor = isSystemActor,
            Module = module,
            RootSubjectType = rootSubjectType,
            RootSubjectId = rootSubjectId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            CorrelationId = correlationId,
            ChangesJson = changesJson,
            Metadata = metadata
        };
}
