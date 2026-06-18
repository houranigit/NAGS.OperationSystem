using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Customer;

namespace Core.Domain.Events;

public sealed class CustomerContactAddedEvent(
    CustomerId customerId,
    CustomerContactId contactId,
    string contactName,
    string contactEmail,
    bool createUser) : DomainEvent
{
    public CustomerId CustomerId { get; } = customerId;
    public CustomerContactId ContactId { get; } = contactId;
    public string ContactName { get; } = contactName;
    public string ContactEmail { get; } = contactEmail;
    public bool CreateUser { get; } = createUser;
}
