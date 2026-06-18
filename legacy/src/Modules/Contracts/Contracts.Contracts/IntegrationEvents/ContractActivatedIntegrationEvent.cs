using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractActivatedIntegrationEvent(
    Guid ContractId,
    bool Automatic) : IntegrationEvent;
