using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record ServiceNameUpdatedIntegrationEvent(
    Guid ServiceId,
    string NewName) : IntegrationEvent;