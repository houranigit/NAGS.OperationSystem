using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserLockedIntegrationEvent(
    Guid UserId,
    string Username,
    DateTime LockedUntil) : IntegrationEvent;
