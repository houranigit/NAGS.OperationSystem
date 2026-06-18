using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;

namespace Identity.Domain.Events;

public sealed class UserRoleAssignedEvent(UserId userId, RoleId roleId) : DomainEvent
{
    public UserId UserId { get; } = userId;
    public RoleId RoleId { get; } = roleId;
}
