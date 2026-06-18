using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.ManpowerPricePlan;

namespace Core.Domain.Events;

public sealed class ManpowerPricePlanCreatedEvent(ManpowerPricePlanId manpowerPricePlanId) : DomainEvent
{
    public ManpowerPricePlanId ManpowerPricePlanId { get; } = manpowerPricePlanId;
}
