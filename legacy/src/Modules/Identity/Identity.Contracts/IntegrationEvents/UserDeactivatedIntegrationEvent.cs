using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserDeactivatedIntegrationEvent(
    Guid UserId,
    string Username) : IntegrationEvent;
