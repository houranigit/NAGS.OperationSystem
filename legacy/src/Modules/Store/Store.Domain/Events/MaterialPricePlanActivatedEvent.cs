using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.MaterialPricePlan;

namespace Store.Domain.Events;

public sealed class MaterialPricePlanActivatedEvent(MaterialPricePlanId materialPricePlanId) : DomainEvent
{
    public MaterialPricePlanId MaterialPricePlanId { get; } = materialPricePlanId;
}
