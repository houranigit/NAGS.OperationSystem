using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.User;

namespace Identity.Domain.Events;

public sealed class UserActivatedEvent(UserId userId) : DomainEvent
{
    public UserId UserId { get; } = userId;
}
