using BuildingBlocks.Infrastructure.Persistence.Attributes;

namespace BuildingBlocks.Infrastructure.Persistence.Models;

[SkipAudit]
public sealed class AuditTrail
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public Guid? CorrelationId { get; set; }
}

public enum AuditAction { Add, Modify, Delete }
