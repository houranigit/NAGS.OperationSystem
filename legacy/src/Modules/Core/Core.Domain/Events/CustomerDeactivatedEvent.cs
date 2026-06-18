using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Customer;

namespace Core.Domain.Events;

public sealed class CustomerDeactivatedEvent(
    CustomerId customerId,
    IReadOnlyList<Guid> contactLinkedUserIds) : DomainEvent
{
    public CustomerId CustomerId { get; } = customerId;
    public IReadOnlyList<Guid> ContactLinkedUserIds { get; } = contactLinkedUserIds;
}
