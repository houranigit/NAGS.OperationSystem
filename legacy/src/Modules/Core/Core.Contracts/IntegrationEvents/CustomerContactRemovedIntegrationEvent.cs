using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record CustomerContactRemovedIntegrationEvent(
    Guid CustomerId,
    Guid ContactId,
    Guid LinkedUserId) : IntegrationEvent;
