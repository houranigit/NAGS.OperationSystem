using BuildingBlocks.Domain.Events;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;

namespace Identity.Domain.Events;

public sealed class UserSessionCreatedEvent(UserSessionId sessionId, UserId userId) : DomainEvent
{
    public UserSessionId SessionId { get; } = sessionId;
    public UserId UserId { get; } = userId;
}
