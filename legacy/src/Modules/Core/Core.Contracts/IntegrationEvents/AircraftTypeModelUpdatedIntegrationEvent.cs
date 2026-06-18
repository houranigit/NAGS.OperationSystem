using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record AircraftTypeModelUpdatedIntegrationEvent(
    Guid AircraftTypeId,
    string NewModel) : IntegrationEvent;
