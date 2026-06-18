using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.Flight.Commands.ClaimAogFlight;

/// <summary>
/// Mobile "Claim flight" action on an AOG flight. Adds the calling employee to the flight's
/// assigned-employee roster (idempotent — re-claims succeed silently). Restricted server-side
/// to flights at the caller's home station that include the AOG service.
/// </summary>
public sealed record ClaimAogFlightCommand(
    Guid FlightId,
    Guid EmployeeId) : ICommand;
