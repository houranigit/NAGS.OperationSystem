namespace OperationsSystem.Blazor.Client.Api;

public sealed record AuditTrailListItem(
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

public sealed record AuditFieldChange(string Field, string? Before, string? After);

public sealed record AuditTrailDetail(
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
    IReadOnlyList<AuditFieldChange> Changes,
    string? Metadata);
