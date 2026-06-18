using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Application.Features.Flight.Mobile;
using Operations.Contracts.IntegrationEvents;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Application.Features.Flight.Commands.InviteEmployeeToFlight;

/// <summary>
/// Loads the flight, looks up the invitee snapshot through Core's <see cref="IEmployeeReader"/>,
/// and asks the aggregate to invite. The aggregate is idempotent (re-invites succeed silently)
/// but still raises the domain event each time. The handler additionally writes a
/// <see cref="FlightEmployeeInvitedIntegrationEvent"/> to the outbox so the
/// Notifications module can produce an inbox entry for the invitee.
/// </summary>
public sealed class InviteEmployeeToFlightCommandHandler(
    IFlightRepository flights,
    IEmployeeReader employeeReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<InviteEmployeeToFlightCommand>
{
    public async Task<Result> Handle(InviteEmployeeToFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");
        if (request.InviteeEmployeeId == Guid.Empty)
            return Error.Validation("Invitee employee id is required.");
        if (request.InviterEmployeeId == Guid.Empty)
            return Error.Validation("Inviter employee id is required.");
        if (request.InviteeEmployeeId == request.InviterEmployeeId)
            return Error.Validation("You cannot invite yourself.");

        var flight = await flights.GetByIdAsync(FlightId.From(request.FlightId), cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        var alreadyAssigned = flight.AssignedEmployees
            .Any(a => a.Employee.EmployeeId == request.InviteeEmployeeId);

        var invitee = await employeeReader.GetByIdAsync(request.InviteeEmployeeId, cancellationToken);
        if (invitee is null)
            return Error.NotFound("Invitee employee not found.");

        var inviteResult = flight.InviteEmployee(invitee, request.InviterEmployeeId);
        if (inviteResult.IsFailure)
            return inviteResult.Error;

        flights.Update(flight);

        // Always emit the integration event — even when the invitee was already on the
        // assigned-employee list — so the notifications module fires for every invite click,
        // matching the mobile requirement that re-invites silently succeed and still notify.
        outboxWriter.Write(
            nameof(FlightEmployeeInvitedIntegrationEvent),
            JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                request.FlightId,
                request.InviterEmployeeId,
                request.InviteeEmployeeId)));

        // Real-time hint: only re-broadcast on first-time invite. Re-invites don't
        // change the assigned-employee list so there's nothing the mobile cache
        // needs to learn about (the inbox notification is the right channel for
        // "your teammate clicked invite again").
        if (!alreadyAssigned)
            FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);

        return Result.Success();
    }
}
