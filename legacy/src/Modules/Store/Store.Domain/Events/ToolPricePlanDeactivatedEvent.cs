using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.ToolPricePlan;

namespace Store.Domain.Events;

public sealed class ToolPricePlanDeactivatedEvent(ToolPricePlanId toolPricePlanId) : DomainEvent
{
    public ToolPricePlanId ToolPricePlanId { get; } = toolPricePlanId;
}
