using Audit.Domain.Enumerations;

namespace Audit.Contracts.SecurityEvent;

/// <summary>List/detail read model aligned with persisted security events.</summary>
public sealed record SecurityEventDto(
    Guid Id,
    SecurityEventType EventType,
    Guid? UserId,
    string? Username,
    string? IpAddress,
    string? Details,
    DateTime OccurredAt);
