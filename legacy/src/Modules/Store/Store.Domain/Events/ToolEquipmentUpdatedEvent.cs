using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.Tool;

namespace Store.Domain.Events;

public sealed class ToolEquipmentUpdatedEvent(ToolId toolId, EquipmentId equipmentId) : DomainEvent
{
    public ToolId ToolId { get; } = toolId;
    public EquipmentId EquipmentId { get; } = equipmentId;
}
