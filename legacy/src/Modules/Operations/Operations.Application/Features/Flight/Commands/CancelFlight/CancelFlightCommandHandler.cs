using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flight.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.ValueObjects;
using DomainWorkOrder = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Features.Flight.Commands.CancelFlight;

/// <summary>
/// Loads the flight, validates that no approval is in place, and creates a cancel work
/// order with no lines and <c>IsCanceled = true</c>. Attaching the work order moves the
/// flight from <c>Scheduled</c> to <c>InProgress</c>; final cancellation only happens
/// when the cancel work order is approved.
/// </summary>
public sealed class CancelFlightCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IOperationsDbContext db,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CancelFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CancelFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");

        // Idempotency short-circuit: the mobile outbox retries with the same client
        // mutation id after ambiguous-timeout failures. If a cancel work order already
        // exists for this key, return it instead of filing a duplicate.
        if (request.ClientMutationId is { } mutationId && mutationId != Guid.Empty)
        {
            var existing = await db.WorkOrders
                .Where(w => w.ClientMutationId == mutationId)
                .Select(w => w.Id.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing != Guid.Empty)
                return existing;
        }

        var flight = await flights.GetByIdAsync(FlightId.From(request.FlightId), cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        if (flight.AcceptedWorkOrder is not null)
            return Error.Conflict("Flight already has an accepted work order. Revoke it before cancelling.");

        var schedule = ScheduledTime.Create(flight.Schedule.Sta, flight.Schedule.Std);
        if (schedule.IsFailure)
            return schedule.Error;

        var customer = flight.Customer with { };
        var station = flight.Station with { };
        var operation = flight.OperationType with { };
        var aircraft = flight.AircraftType is null
            ? null
            : flight.AircraftType with { };

        var created = DomainWorkOrder.CreateWithFlight(
            flight.Id,
            customer,
            station,
            operation,
            flight.FlightNumber,
            aircraft,
            aircraftTailNumber: null,
            schedule.Value!,
            serviceLines: Array.Empty<WorkOrderServiceLineInput>(),
            tasks: Array.Empty<WorkOrderTaskInput>(),
            isCanceled: true,
            cancellationAt: request.CanceledAt,
            actualTime: null,
            utcNow: DateTimeOffset.UtcNow,
            createdByEmployeeId: request.CreatedByEmployeeId,
            clientMutationId: request.ClientMutationId);

        if (created.IsFailure)
            return created.Error;

        var workOrder = created.Value!;
        workOrders.Add(workOrder);

        var attach = flight.AttachWorkOrder(workOrder.Id);
        if (attach.IsFailure)
            return attach.Error;
        flights.Update(flight);

        // Cancel pushes the flight to InProgress (pending the cancel-WO approval).
        // Mobile shows the new status the moment the live event arrives; the actual
        // row removal from "my flights" only happens when the cancel work order is
        // approved and the flight transitions to Canceled — that emits its own event.
        FlightMobileSyncBroadcasts.EnqueueUpsert(
            mobileSync,
            flight,
            originMutationId: workOrder.ClientMutationId?.ToString());

        return workOrder.Id.Value;
    }
}
