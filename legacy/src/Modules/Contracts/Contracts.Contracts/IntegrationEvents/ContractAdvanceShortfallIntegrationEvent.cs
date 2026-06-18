using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractAdvanceShortfallIntegrationEvent(
    Guid ContractId,
    decimal ShortfallAmount) : IntegrationEvent;
