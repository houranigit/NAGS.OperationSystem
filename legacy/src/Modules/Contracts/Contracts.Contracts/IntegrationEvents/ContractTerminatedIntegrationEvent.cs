using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractTerminatedIntegrationEvent(
    Guid ContractId,
    string Reason) : IntegrationEvent;
