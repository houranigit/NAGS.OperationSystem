using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserLoggedInIntegrationEvent(
    Guid UserId,
    string Username,
    string? IpAddress,
    string? DeviceInfo) : IntegrationEvent;
