using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.Role;

namespace Identity.Domain.Events;

public sealed class RoleCreatedEvent(RoleId roleId) : DomainEvent
{
    public RoleId RoleId { get; } = roleId;
}
