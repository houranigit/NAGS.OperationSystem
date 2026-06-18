using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.User;

namespace Identity.Domain.Events;

public sealed class UserLockedEvent(UserId userId, DateTime lockedUntil) : DomainEvent
{
    public UserId UserId { get; } = userId;
    public DateTime LockedUntil { get; } = lockedUntil;
}
