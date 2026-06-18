using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.User;
using Identity.Domain.ValueObjects;

namespace Identity.Domain.Events;

public sealed class UserEmailChangedEvent(UserId userId, Email oldEmail, Email newEmail) : DomainEvent
{
    public UserId UserId { get; } = userId;
    public Email OldEmail { get; } = oldEmail;
    public Email NewEmail { get; } = newEmail;
}
