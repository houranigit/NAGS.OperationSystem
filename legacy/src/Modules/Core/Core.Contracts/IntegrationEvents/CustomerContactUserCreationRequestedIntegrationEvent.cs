using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record CustomerContactUserCreationRequestedIntegrationEvent(
    Guid CustomerId,
    Guid ContactId,
    string ContactName,
    string ContactEmail) : IntegrationEvent;
