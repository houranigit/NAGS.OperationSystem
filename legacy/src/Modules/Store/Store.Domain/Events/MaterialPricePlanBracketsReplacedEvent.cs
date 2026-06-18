using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.MaterialPricePlan;

namespace Store.Domain.Events;

public sealed class MaterialPricePlanBracketsReplacedEvent(MaterialPricePlanId materialPricePlanId) : DomainEvent
{
    public MaterialPricePlanId MaterialPricePlanId { get; } = materialPricePlanId;
}
