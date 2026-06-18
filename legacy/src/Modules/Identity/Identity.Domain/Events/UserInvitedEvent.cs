using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.User;

namespace Identity.Domain.Events;

public sealed class UserInvitedEvent(UserId userId, string email, string invitationToken) : DomainEvent
{
    public UserId UserId { get; } = userId;
    public string Email { get; } = email;
    public string InvitationToken { get; } = invitationToken;
}
