using BuildingBlocks.Contracts.IntegrationEvents;

namespace Operations.Contracts.IntegrationEvents;

public sealed record FlightEmployeeInvitedIntegrationEvent(
    Guid FlightId,
    Guid InviterEmployeeId,
    Guid InviteeEmployeeId) : IntegrationEvent;
