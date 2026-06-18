using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Operations.Application.Features.Flight.Mobile;
using Operations.Contracts.IntegrationEvents;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Application.Features.Flight.Commands.InviteEmployeesToFlight;

/// <summary>
/// Loads the flight once, resolves each invitee snapshot through Core's
/// <see cref="IEmployeeReader"/>, and invites them all on the aggregate in memory before a
/// single <see cref="IFlightRepository.Update"/> + sync broadcast. Mirrors the idempotency
/// rules of the single-invite handler: self-invites and already-assigned employees are
/// skipped, but every successfully-resolved invitee still emits a
/// <see cref="FlightEmployeeInvitedIntegrationEvent"/> so the Notifications module produces
/// an inbox entry for each invite click.
/// </summary>
public sealed class InviteEmployeesToFlightCommandHandler(
    IFlightRepository flights,
    IEmployeeReader employeeReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<InviteEmployeesToFlightCommand>
{
    public async Task<Result> Handle(InviteEmployeesToFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");
        if (request.InviterEmployeeId == Guid.Empty)
            return Error.Validation("Inviter employee id is required.");
        if (request.InviteeEmployeeIds is null || request.InviteeEmployeeIds.Count == 0)
            return Error.Validation("At least one invitee is required.");

        // De-dupe, drop empties, and never invite the inviter to themselves.
        var inviteeIds = request.InviteeEmployeeIds
            .Where(id => id != Guid.Empty && id != request.InviterEmployeeId)
            .Distinct()
            .ToList();
        if (inviteeIds.Count == 0)
            return Error.Validation("At least one valid invitee is required.");

        var flight = await flights.GetByIdAsync(FlightId.From(request.FlightId), cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        var anyNewlyAssigned = false;

        foreach (var inviteeId in inviteeIds)
        {
            var alreadyAssigned = flight.AssignedEmployees
                .Any(a => a.Employee.EmployeeId == inviteeId);

            var invitee = await employeeReader.GetByIdAsync(inviteeId, cancellationToken);
            if (invitee is null)
                return Error.NotFound($"Invitee employee {inviteeId} not found.");

            var inviteResult = flight.InviteEmployee(invitee, request.InviterEmployeeId);
            if (inviteResult.IsFailure)
                return inviteResult.Error;

            // Emit the integration event for every invite click — even when the invitee was
            // already on the assigned-employee list — matching the single-invite behaviour.
            outboxWriter.Write(
                nameof(FlightEmployeeInvitedIntegrationEvent),
                JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                    request.FlightId,
                    request.InviterEmployeeId,
                    inviteeId)));

            if (!alreadyAssigned)
                anyNewlyAssigned = true;
        }

        flights.Update(flight);

        // Single broadcast for the whole batch — only when the roster actually changed, so
        // re-inviting an existing crew member doesn't churn every assigned device's cache.
        if (anyNewlyAssigned)
            FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);

        return Result.Success();
    }
}
