using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.Flight.Commands.InviteEmployeesToFlight;

/// <summary>
/// Batch variant of <see cref="Operations.Application.Features.Flight.Commands.InviteEmployeeToFlight.InviteEmployeeToFlightCommand"/>:
/// the inviter (calling employee) adds a whole list of employees to the flight's assigned
/// crew in a single command. The handler loads the flight once and saves once — there is no
/// per-employee command dispatch. Each invitee is processed idempotently (re-invites and
/// self-invites are skipped) and still produces a notification integration event.
/// </summary>
public sealed record InviteEmployeesToFlightCommand(
    Guid FlightId,
    IReadOnlyList<Guid> InviteeEmployeeIds,
    Guid InviterEmployeeId) : ICommand;
