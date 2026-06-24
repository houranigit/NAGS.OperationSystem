using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Auditing;

namespace BuildingBlocks.Application.Auditing;

/// <summary>
/// Enqueues an explicit business/security audit event into the calling module's outbox in the same
/// transaction as the change. Use for facts that are not a plain entity field delta (logins,
/// invitations, lifecycle actions, role/permission changes, portal access, MFA).
/// </summary>
public static class AuditEnqueueExtensions
{
    public static void EnqueueAudit(
        this IOutboxDbContext db,
        IAuditContext auditContext,
        string module,
        string rootSubjectType,
        Guid? rootSubjectId,
        string entityType,
        Guid? entityId,
        string action,
        IReadOnlyList<AuditFieldChange>? changes = null,
        string? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(auditContext);

        var safeChanges = (changes ?? [])
            .Where(c => !AuditRedaction.IsSensitive(c.Field))
            .ToList();

        db.Enqueue(new AuditEntryRecorded
        {
            ActorId = auditContext.ActorId,
            ActorDisplayName = auditContext.ActorDisplayName,
            IsSystemActor = auditContext.IsSystemActor,
            Module = module,
            RootSubjectType = rootSubjectType,
            RootSubjectId = rootSubjectId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            CorrelationId = auditContext.CorrelationId,
            Changes = safeChanges,
            Metadata = metadata
        });
    }
}
