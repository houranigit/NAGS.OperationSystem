namespace Audit.Application.Contracts;

public sealed record AuditFieldChangeDto(string Field, string? Before, string? After);

public sealed record AuditTrailListItemDto(
    Guid Id,
    DateTimeOffset OccurredOnUtc,
    Guid? ActorId,
    string? ActorDisplayName,
    bool IsSystemActor,
    string Module,
    string RootSubjectType,
    Guid? RootSubjectId,
    string EntityType,
    Guid? EntityId,
    string Action,
    string? CorrelationId);

public sealed record AuditTrailDto(
    Guid Id,
    DateTimeOffset OccurredOnUtc,
    Guid? ActorId,
    string? ActorDisplayName,
    bool IsSystemActor,
    string Module,
    string RootSubjectType,
    Guid? RootSubjectId,
    string EntityType,
    Guid? EntityId,
    string Action,
    string? CorrelationId,
    IReadOnlyList<AuditFieldChangeDto> Changes,
    string? Metadata);
