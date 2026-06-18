using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Customer;

namespace Core.Domain.Events;

public sealed class CustomerCreatedEvent(CustomerId customerId) : DomainEvent
{
    public CustomerId CustomerId { get; } = customerId;
}
