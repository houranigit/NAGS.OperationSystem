using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record LicenseNameUpdatedIntegrationEvent(
    Guid LicenseId,
    string NewName) : IntegrationEvent;
