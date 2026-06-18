using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserActivatedIntegrationEvent(
    Guid UserId,
    string Username) : IntegrationEvent;
