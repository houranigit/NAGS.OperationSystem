using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Customer;

namespace Core.Domain.Events;

public sealed class CustomerActivatedEvent(CustomerId customerId) : DomainEvent
{
    public CustomerId CustomerId { get; } = customerId;
}
