namespace BuildingBlocks.Infrastructure.Persistence.Attributes;

/// <summary>
/// Entities decorated with this attribute are excluded from the automatic audit trail.
/// Apply to OutboxMessage, InboxMessage, AuditTrail itself, and any other infrastructure entities.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipAuditAttribute : Attribute { }
