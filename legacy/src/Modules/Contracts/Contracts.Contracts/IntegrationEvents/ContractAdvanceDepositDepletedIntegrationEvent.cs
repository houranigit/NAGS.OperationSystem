using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractAdvanceDepositDepletedIntegrationEvent(Guid ContractId) : IntegrationEvent;
