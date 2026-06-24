namespace BuildingBlocks.Domain.Auditing;

/// <summary>
/// Opt-in marker for entities whose create/update changes are captured automatically into the
/// permanent audit trail. Implementers expose the stable identifiers used to group history and
/// to walk a child entity back up to the business "root" subject it belongs to (for example a
/// customer contact rolls up to its customer). For a root aggregate the root identifiers simply
/// equal the entity's own identifiers.
/// </summary>
public interface IAuditable
{
    /// <summary>Stable type name of this entity as it appears in the audit trail (e.g. "User").</summary>
    public string AuditEntityType { get; }

    /// <summary>The entity's own id.</summary>
    public Guid AuditEntityId { get; }

    /// <summary>Type name of the business root this entity belongs to (often the same as the entity).</summary>
    public string AuditRootType => AuditEntityType;

    /// <summary>Id of the business root this entity belongs to (often the entity's own id).</summary>
    public Guid AuditRootId => AuditEntityId;
}
