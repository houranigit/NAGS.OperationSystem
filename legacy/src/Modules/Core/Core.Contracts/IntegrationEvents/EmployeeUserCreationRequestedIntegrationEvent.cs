using BuildingBlocks.Contracts.IntegrationEvents;

namespace Core.Contracts.IntegrationEvents;

public sealed record EmployeeUserCreationRequestedIntegrationEvent(
    Guid EmployeeId,
    string FullName,
    string Email) : IntegrationEvent;
