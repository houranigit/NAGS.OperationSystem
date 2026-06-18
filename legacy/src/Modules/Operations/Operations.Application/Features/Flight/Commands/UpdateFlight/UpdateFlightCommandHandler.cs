using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Contracts.Readers;
using Core.Contracts.Seeding;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Application.Features.Flight.Mobile;
using Operations.Contracts.IntegrationEvents;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flight.Commands.UpdateFlight;

/// <summary>
/// Loads the flight aggregate, re-resolves the contract for the updated
/// (Customer, Station, OT, STA) tuple, then delegates to the aggregate's
/// <see cref="Operations.Domain.Aggregates.Flight.Flight.UpdateOperationalDetails"/>.
/// Never mutates status, cancellation, accepted or attached work orders.
/// </summary>
public sealed class UpdateFlightCommandHandler(
    IFlightRepository flights,
    IContractReadService contractReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateFlightCommand>
{
    public async Task<Result> Handle(UpdateFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return Result.Failure(Error.Validation("Flight id is required."));

        if (request.OperationTypeSnapshot.OperationTypeId == CoreSeedIds.AdHocOperationType)
            return Result.Failure(Error.Validation(
                "Ad Hoc operation type is not allowed for scheduled flights."));

        var flightId = FlightId.From(request.Id);
        var flight = await flights.GetByIdAsync(flightId, cancellationToken);
        if (flight is null)
            return Result.Failure(Error.NotFound("Flight not found."));

        if (flight.Status != FlightStatus.Scheduled)
            return Result.Failure(Error.Validation(
                "Only flights in Scheduled status can be edited."));

        var number = FlightNumber.Create(request.FlightNumber);
        if (number.IsFailure)
            return Result.Failure(number.Error);

        var schedule = ScheduledTime.Create(request.Sta, request.Std);
        if (schedule.IsFailure)
            return Result.Failure(schedule.Error);

        var resolved = await contractReader.FindActiveContractForFlightAsync(
            request.CustomerSnapshot.CustomerId,
            request.StationSnapshot.StationId,
            request.OperationTypeSnapshot.OperationTypeId,
            request.Sta,
            cancellationToken);

        switch (resolved.Outcome)
        {
            case FindContractOutcome.NotFound:
                return Result.Failure(Error.Validation(
                    "No active contract covers this customer / station / operation type at the scheduled time."));
            case FindContractOutcome.Ambiguous:
                return Result.Failure(Error.Conflict(
                    "Multiple active contracts cover this slot — please clean up overlapping contracts before updating the flight."));
        }

        var contract = resolved.Contract!;

        var previousIds = flight.AssignedEmployees
            .Select(x => x.Employee.EmployeeId)
            .ToHashSet();

        var updated = flight.UpdateOperationalDetails(
            contract.ContractId,
            contract.ContractNumber,
            request.CustomerSnapshot,
            request.StationSnapshot,
            request.OperationTypeSnapshot,
            number.Value,
            schedule.Value,
            request.AircraftTypeId,
            contract.OperationTypeServices,
            request.AssignedEmployees,
            assignmentRequired: false,
            actingEmployeeId: Guid.Empty);

        if (updated.IsFailure)
            return updated;

        flights.Update(flight);

        // Mirror the aggregate's invite events with outbox integration events for any newly
        // added employees, so the notifications module fires the same way as InviteEmployee.
        foreach (var emp in flight.AssignedEmployees)
        {
            if (!previousIds.Contains(emp.Employee.EmployeeId))
            {
                outboxWriter.Write(
                    nameof(FlightEmployeeInvitedIntegrationEvent),
                    JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                        flight.Id.Value,
                        InviterEmployeeId: Guid.Empty,
                        InviteeEmployeeId: emp.Employee.EmployeeId)));
            }
        }

        // Real-time hint for connected mobile clients: assignees may have changed
        // (added or removed), AOG/station may have shifted. The helper fans out to
        // currently-assigned employees + the station group; employees who lost the
        // flight from their roster get a per-employee delete to clear their cache.
        FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);
        foreach (var previousId in previousIds)
        {
            if (flight.AssignedEmployees.All(a => a.Employee.EmployeeId != previousId))
                FlightMobileSyncBroadcasts.EnqueueFlightForEmployee(
                    mobileSync, flight, previousId, MobileSyncOps.Delete);
        }

        return Result.Success();
    }
}
