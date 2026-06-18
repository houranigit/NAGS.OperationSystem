using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Application.Features.Flight.Mobile;
using Operations.Contracts.IntegrationEvents;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Application.Features.Flight.Commands.ClaimAogFlight;

/// <summary>
/// Loads the flight, validates that it (a) is at the caller's home station and (b) carries
/// an AOG service, then asks the aggregate to assign the caller. Mirrors the invite-event
/// outbox write so the same notifications path fires as for a regular invite.
/// </summary>
public sealed class ClaimAogFlightCommandHandler(
    IFlightRepository flights,
    IEmployeeReader employeeReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<ClaimAogFlightCommand>
{
    public async Task<Result> Handle(ClaimAogFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var flight = await flights.GetByIdAsync(FlightId.From(request.FlightId), cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        var employee = await employeeReader.GetByIdAsync(request.EmployeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("Employee not found.");

        if (employee.StationSnapshot.StationId != flight.Station.StationId)
            return Error.Conflict("This flight is at a different station.");

        if (!flight.Services.Any(s => s.Service.IsAog))
            return Error.Conflict("This flight does not carry an AOG service. Use the regular invite flow.");

        var alreadyAssigned = flight.AssignedEmployees.Any(a => a.Employee.EmployeeId == request.EmployeeId);

        var assign = flight.AssignEmployee(employee);
        if (assign.IsFailure)
            return assign.Error;

        flights.Update(flight);

        // Don't double-notify on idempotent re-claim — only emit when the assign actually
        // added a new employee.
        if (!alreadyAssigned)
        {
            outboxWriter.Write(
                nameof(FlightEmployeeInvitedIntegrationEvent),
                JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                    flight.Id.Value,
                    InviterEmployeeId: Guid.Empty,
                    InviteeEmployeeId: request.EmployeeId)));

            // The claim moves this AOG flight onto the claimant's "my flights" list —
            // push to that employee specifically. We also re-broadcast to the station
            // group so every AOG-tab on the station refreshes the assignee count.
            FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);
        }

        return Result.Success();
    }
}
