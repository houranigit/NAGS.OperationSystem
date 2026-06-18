using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.Flight.Commands.InviteEmployeeToFlight;

/// <summary>
/// Inviter (the calling employee) adds another employee to the flight's assigned crew.
/// Wraps <see cref="Operations.Domain.Aggregates.Flight.Flight.InviteEmployee"/> which is
/// idempotent — if the invitee is already on the assigned-employee list the command still
/// succeeds and a domain event is still raised so a notification fires either way (mobile
/// requirement: "if the invited employee was already in assigned employee list … only
/// return a success and fire the notification event").
/// </summary>
public sealed record InviteEmployeeToFlightCommand(
    Guid FlightId,
    Guid InviteeEmployeeId,
    Guid InviterEmployeeId) : ICommand;
