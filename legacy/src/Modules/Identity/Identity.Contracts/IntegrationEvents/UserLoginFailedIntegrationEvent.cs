using BuildingBlocks.Contracts.IntegrationEvents;

namespace Identity.Contracts.IntegrationEvents;

public sealed record UserLoginFailedIntegrationEvent(
    string EmailOrUsername,
    string? IpAddress,
    string Reason) : IntegrationEvent;
