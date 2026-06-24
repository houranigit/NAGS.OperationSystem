namespace BuildingBlocks.Application.Auditing;

/// <summary>
/// Default <see cref="IAuditContext"/> used when there is no request-scoped actor (background jobs,
/// seeding, tests). The API host overrides this with a claims-backed implementation.
/// </summary>
public sealed class SystemAuditContext : IAuditContext
{
    public Guid? ActorId => null;
    public string? ActorDisplayName => null;
    public bool IsSystemActor => true;
    public string? CorrelationId => null;
}
