namespace Audit.Domain.Authorization;

/// <summary>
/// Audit module permissions. Reading the audit trail is System Administrator-only; there are no
/// write or delete permissions because the trail is append-only and immutable.
/// </summary>
public static class AuditPermissions
{
    public static class Trails
    {
        public const string View = "audit.trails.view";
    }

    public static IReadOnlyList<string> All { get; } = [Trails.View];
}
