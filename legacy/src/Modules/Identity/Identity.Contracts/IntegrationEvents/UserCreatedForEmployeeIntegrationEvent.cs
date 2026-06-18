using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

/// <summary>
/// Reply emitted by Identity after it provisions a user in response to
/// <c>EmployeeUserCreationRequestedIntegrationEvent</c>. Core consumes it to set
/// <c>Employee.LinkedUserId</c>, closing the create-employee-with-account saga.
/// </summary>
public sealed record UserCreatedForEmployeeIntegrationEvent(
    Guid EmployeeId,
    Guid UserId) : IntegrationEvent;
