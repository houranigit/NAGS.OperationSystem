using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserCreatedIntegrationEvent(
    Guid UserId,
    string Username,
    string Email,
    string UserType) : IntegrationEvent;
