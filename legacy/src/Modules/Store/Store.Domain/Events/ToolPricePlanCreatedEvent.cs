using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.ToolPricePlan;

namespace Store.Domain.Events;

public sealed class ToolPricePlanCreatedEvent(ToolPricePlanId toolPricePlanId) : DomainEvent
{
    public ToolPricePlanId ToolPricePlanId { get; } = toolPricePlanId;
}
