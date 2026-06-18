using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record CustomerIataCodeUpdatedIntegrationEvent(
    Guid CustomerId,
    string NewIataCode) : IntegrationEvent;
