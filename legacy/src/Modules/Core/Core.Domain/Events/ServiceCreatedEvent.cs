using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Service;

namespace Core.Domain.Events;

public sealed class ServiceCreatedEvent(ServiceId serviceId) : DomainEvent
{
    public ServiceId ServiceId { get; } = serviceId;
}
