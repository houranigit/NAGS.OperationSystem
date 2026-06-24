using BuildingBlocks.Contracts.Messaging;

namespace BuildingBlocks.Contracts.Auditing;

/// <summary>
/// Standard action names recorded on an audit trail. Automatic capture uses the generic verbs;
/// explicit business/security events may use a more specific action.
/// </summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string Activated = "Activated";
    public const string Deactivated = "Deactivated";
    public const string Suspended = "Suspended";
    public const string AccessRestored = "AccessRestored";
    public const string Invited = "Invited";
    public const string InvitationResent = "InvitationResent";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string PasswordChanged = "PasswordChanged";
    public const string RoleAssigned = "RoleAssigned";
    public const string PermissionsChanged = "PermissionsChanged";
    public const string SessionsRevoked = "SessionsRevoked";
    public const string PortalAccessGranted = "PortalAccessGranted";
    public const string MfaReset = "MfaReset";
}

/// <summary>A single field-level before/after change. Values are already redacted by the producer.</summary>
public sealed record AuditFieldChange(string Field, string? Before, string? After);

/// <summary>
/// A cross-cutting, append-only audit fact. Written to the originating module's outbox in the same
/// transaction as the business change (so an audit record cannot be lost while the change commits),
/// and persisted by the Audit module. The event lives in BuildingBlocks so the automatic-capture
/// interceptor and every module can produce it without depending on the Audit module.
/// Secrets (passwords, hashes, tokens, MFA secrets, refresh tokens, credentials) must never appear
/// in <see cref="Changes"/> or <see cref="Metadata"/>.
/// </summary>
public sealed record AuditEntryRecorded : IntegrationEvent
{
    public Guid? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public bool IsSystemActor { get; init; }

    public required string Module { get; init; }
    public required string RootSubjectType { get; init; }
    public Guid? RootSubjectId { get; init; }
    public required string EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public required string Action { get; init; }

    public string? CorrelationId { get; init; }

    public IReadOnlyList<AuditFieldChange> Changes { get; init; } = [];

    /// <summary>Optional non-sensitive context (e.g. reason, ip) for explicit security events.</summary>
    public string? Metadata { get; init; }
}
