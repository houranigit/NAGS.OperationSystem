using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Customer;

namespace Core.Domain.Events;

public sealed class CustomerContactLinkedToUserEvent(
    CustomerId customerId,
    CustomerContactId contactId,
    Guid linkedUserId) : DomainEvent
{
    public CustomerId CustomerId { get; } = customerId;
    public CustomerContactId ContactId { get; } = contactId;
    public Guid LinkedUserId { get; } = linkedUserId;
}
