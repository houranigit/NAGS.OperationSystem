using BuildingBlocks.Contracts.IntegrationEvents;

namespace Contracts.Contracts.IntegrationEvents;

public sealed record ContractExpiringSoonIntegrationEvent(
    Guid ContractId,
    string ContractNo,
    Guid CustomerId,
    DateTimeOffset ExpiryDate,
    int DaysUntilExpiry) : IntegrationEvent;
