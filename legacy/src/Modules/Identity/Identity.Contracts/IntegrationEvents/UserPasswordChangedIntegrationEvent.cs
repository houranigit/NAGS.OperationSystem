using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserPasswordChangedIntegrationEvent(
    Guid UserId,
    string Username) : IntegrationEvent;
