using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record CustomerDeactivatedIntegrationEvent(
    Guid CustomerId,
    IReadOnlyList<Guid> ContactLinkedUserIds) : IntegrationEvent;
