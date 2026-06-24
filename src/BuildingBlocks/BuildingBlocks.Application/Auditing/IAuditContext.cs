namespace BuildingBlocks.Application.Auditing;

/// <summary>
/// The acting principal and correlation context for audit capture, resolved per request. Kept
/// separate from <see cref="Abstractions.IUserContext"/> so infrastructure (the capture interceptor)
/// can record the actor and correlation id without taking an ASP.NET Core dependency.
/// </summary>
public interface IAuditContext
{
    public Guid? ActorId { get; }

    public string? ActorDisplayName { get; }

    /// <summary>True when there is no authenticated user (background jobs, seeding, system actions).</summary>
    public bool IsSystemActor { get; }

    public string? CorrelationId { get; }
}
