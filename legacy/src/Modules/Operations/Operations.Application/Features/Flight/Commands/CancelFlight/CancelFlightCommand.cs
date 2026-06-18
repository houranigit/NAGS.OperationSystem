using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.Flight.Commands.CancelFlight;

/// <summary>
/// Files an empty cancel work order against the flight: only the cancellation timestamp
/// is captured (no service / employee / corrective-action lines). The work order enters
/// <c>UnderReview</c> and follows the same Approve / Revoke flow as a normal work order;
/// approving it settles the flight as Canceled.
/// </summary>
public sealed record CancelFlightCommand(
    Guid FlightId,
    DateTimeOffset CanceledAt,
    Guid? CreatedByEmployeeId = null,
    Guid? ClientMutationId = null) : ICommand<Guid>;
