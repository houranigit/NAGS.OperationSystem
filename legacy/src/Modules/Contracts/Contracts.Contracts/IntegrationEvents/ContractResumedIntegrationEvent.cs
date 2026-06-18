using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractResumedIntegrationEvent(Guid ContractId) : IntegrationEvent;
