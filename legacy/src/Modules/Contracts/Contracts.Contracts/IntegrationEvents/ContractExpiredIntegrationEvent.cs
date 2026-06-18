using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractExpiredIntegrationEvent(Guid ContractId) : IntegrationEvent;
