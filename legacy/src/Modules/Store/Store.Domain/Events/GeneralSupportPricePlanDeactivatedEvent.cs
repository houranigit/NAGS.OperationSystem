using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.GeneralSupportPricePlan;

namespace Store.Domain.Events;

public sealed class GeneralSupportPricePlanDeactivatedEvent(GeneralSupportPricePlanId generalSupportPricePlanId) : DomainEvent
{
    public GeneralSupportPricePlanId GeneralSupportPricePlanId { get; } = generalSupportPricePlanId;
}
