namespace Audit.Contracts;

/// <summary>
/// Stable identifiers for the Audit module. The cross-cutting audit event itself lives in
/// <c>BuildingBlocks.Contracts.Auditing</c> so the capture interceptor and every module can produce
/// it without referencing the Audit module; this contracts assembly is reserved for future
/// audit-specific cross-module contracts.
/// </summary>
public static class AuditModule
{
    public const string Name = "audit";
}
