using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record EmployeeDeactivatedIntegrationEvent(
    Guid EmployeeId,
    Guid LinkedUserId) : IntegrationEvent;
