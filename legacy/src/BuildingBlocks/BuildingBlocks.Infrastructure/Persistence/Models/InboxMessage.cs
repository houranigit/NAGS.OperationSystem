using BuildingBlocks.Infrastructure.Persistence.Attributes;

namespace BuildingBlocks.Infrastructure.Persistence.Models;

[SkipAudit]
public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
