using BuildingBlocks.Domain.Events;

namespace Identity.Domain.Roles.Events;

public sealed record RoleCreatedEvent(Guid RoleId, string Name) : DomainEvent;

public sealed record RoleUpdatedEvent(Guid RoleId) : DomainEvent;

public sealed record RolePermissionsChangedEvent(Guid RoleId, IReadOnlyList<string> Permissions) : DomainEvent;
