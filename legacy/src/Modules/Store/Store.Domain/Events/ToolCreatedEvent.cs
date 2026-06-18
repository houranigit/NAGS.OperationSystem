using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.Tool;

namespace Store.Domain.Events;

public sealed class ToolCreatedEvent(ToolId toolId) : DomainEvent
{
    public ToolId ToolId { get; } = toolId;
}
