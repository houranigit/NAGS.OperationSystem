using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.ManpowerPricePlan;

namespace Core.Domain.Events;

public sealed class ManpowerPricePlanActivatedEvent(ManpowerPricePlanId manpowerPricePlanId) : DomainEvent
{
    public ManpowerPricePlanId ManpowerPricePlanId { get; } = manpowerPricePlanId;
}
