using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractSuspendedIntegrationEvent(
    Guid ContractId,
    string Reason) : IntegrationEvent;
