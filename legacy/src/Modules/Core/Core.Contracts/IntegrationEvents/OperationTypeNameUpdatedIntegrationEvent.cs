using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record OperationTypeNameUpdatedIntegrationEvent(
    Guid OperationTypeId,
    string NewName) : IntegrationEvent;
