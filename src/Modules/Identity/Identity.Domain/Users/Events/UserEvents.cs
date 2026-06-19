using BuildingBlocks.Domain.Events;

namespace Identity.Domain.Users.Events;

public sealed record UserInvitedEvent(Guid UserId, string Email, Guid RoleId) : DomainEvent;

public sealed record UserActivatedEvent(Guid UserId) : DomainEvent;

public sealed record UserPasswordChangedEvent(Guid UserId) : DomainEvent;

public sealed record UserRoleAssignedEvent(Guid UserId, Guid RoleId) : DomainEvent;

public sealed record UserLockedEvent(Guid UserId, DateTimeOffset? LockoutEndUtc) : DomainEvent;

public sealed record UserUnlockedEvent(Guid UserId) : DomainEvent;

public sealed record UserDeactivatedEvent(Guid UserId) : DomainEvent;

public sealed record UserProfileUpdatedEvent(Guid UserId) : DomainEvent;
