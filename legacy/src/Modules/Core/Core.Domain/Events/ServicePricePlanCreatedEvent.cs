using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.ServicePricePlan;

namespace Core.Domain.Events;

public sealed class ServicePricePlanCreatedEvent(ServicePricePlanId servicePricePlanId) : DomainEvent
{
    public ServicePricePlanId ServicePricePlanId { get; } = servicePricePlanId;
}
