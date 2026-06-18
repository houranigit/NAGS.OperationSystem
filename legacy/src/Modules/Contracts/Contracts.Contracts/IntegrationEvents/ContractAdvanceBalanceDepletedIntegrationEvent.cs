using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractAdvanceBalanceDepletedIntegrationEvent(Guid ContractId) : IntegrationEvent;
