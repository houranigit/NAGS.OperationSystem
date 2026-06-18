using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Service;

namespace Core.Domain.Events;

public sealed class ServiceActivatedEvent(ServiceId serviceId) : DomainEvent
{
    public ServiceId ServiceId { get; } = serviceId;
}
