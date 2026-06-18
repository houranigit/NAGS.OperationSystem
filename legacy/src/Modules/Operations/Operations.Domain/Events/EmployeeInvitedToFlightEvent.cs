using BuildingBlocks.Domain.Events;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Events;

public sealed class EmployeeInvitedToFlightEvent(
    FlightId flightId,
    Guid inviterEmployeeId,
    Guid inviteeEmployeeId) : DomainEvent
{
    public FlightId FlightId { get; } = flightId;
    public Guid InviterEmployeeId { get; } = inviterEmployeeId;
    public Guid InviteeEmployeeId { get; } = inviteeEmployeeId;
}
