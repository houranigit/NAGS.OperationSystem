using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flight.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.ValueObjects;
using DomainWorkOrder = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;

/// <summary>
/// Loads the flight, builds a fresh work order via <see cref="DomainWorkOrder.CreateWithFlight"/>
/// (snapshots cloned from the flight to satisfy EF's owned-type single-owner rule), then
/// attaches it through <see cref="Flight.AttachWorkOrder"/> which transitions the flight
/// from <c>Scheduled</c> to <c>InProgress</c>. Refuses to add when the flight is already
/// settled (<c>AcceptedWorkOrder</c> set) — the flight must be revoked first.
/// </summary>
/// <remarks>
/// Service lines and tasks are both optional. Service rows resolve their service and
/// employee snapshots through Core readers; task rows resolve participating employees
/// (must be active in Core) plus optional tools / materials / general-supports from Store.
/// <para>
/// Idempotency: when <see cref="CreateWorkOrderForFlightCommand.ClientMutationId"/> is
/// supplied (mobile outbox path) and a work order with that key already exists, the
/// handler returns the existing ids and skips creation entirely. This makes retries after
/// ambiguous-timeout failures duplicate-safe — see <c>WorkOrder.ClientMutationId</c> and
/// the filtered-unique index in <c>WorkOrderConfiguration</c>.
/// </para>
/// </remarks>
public sealed class CreateWorkOrderForFlightCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IAircraftTypeReader aircraftTypeReader,
    Operations.Application.Features.WorkOrder.WorkOrderInputBuilder inputBuilder,
    IOperationsDbContext db,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateWorkOrderForFlightCommand, CreateWorkOrderForFlightResult>
{
    public async Task<Result<CreateWorkOrderForFlightResult>> Handle(CreateWorkOrderForFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");

        // Idempotency short-circuit: if the same client mutation already produced a work
        // order, return the existing ids. The mobile outbox retries with the same key after
        // ambiguous-timeout failures (server received the create, response was dropped),
        // and this guard is the only thing that keeps a single offline action from
        // producing two server-side work orders. Lookup is by indexed column so the cost
        // is one seek per request — well worth the safety.
        if (request.ClientMutationId is { } mutationId && mutationId != Guid.Empty)
        {
            var existing = await db.WorkOrders
                .Where(w => w.ClientMutationId == mutationId)
                .Select(w => new { WoId = w.Id.Value, FlightId = w.FlightId == null ? (Guid?)null : w.FlightId.Value })
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return new CreateWorkOrderForFlightResult(
                    WorkOrderId: existing.WoId,
                    FlightId: existing.FlightId ?? request.FlightId,
                    Idempotent: true);
            }
        }

        var flightId = FlightId.From(request.FlightId);
        var flight = await flights.GetByIdAsync(flightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        if (flight.AcceptedWorkOrder is not null)
            return Error.Conflict("Flight already has an accepted work order. Revoke it before adding a new one.");

        var flightNumber = FlightNumber.Create(request.FlightNumber);
        if (flightNumber.IsFailure)
            return flightNumber.Error;

        var schedule = ScheduledTime.Create(flight.Schedule.Sta, flight.Schedule.Std);
        if (schedule.IsFailure)
            return schedule.Error;

        ActualTime? actualTime = null;
        if (!request.IsCanceled)
        {
            if (request.Ata is null || request.Atd is null)
                return Error.Validation("ATA and ATD are required when the work order is not a cancellation.");
            var actuals = ActualTime.Create(request.Ata.Value, request.Atd.Value);
            if (actuals.IsFailure)
                return actuals.Error;
            actualTime = actuals.Value!;
        }

        AircraftTypeSnapshot? aircraftType;
        if (request.AircraftTypeId is { } aircraftTypeId)
        {
            var snap = await aircraftTypeReader.GetByIdAsync(aircraftTypeId, cancellationToken);
            if (snap is null)
                return Error.Validation("Aircraft type not found.");
            aircraftType = new AircraftTypeSnapshot(snap.AircraftTypeId, snap.Model);
        }
        else if (flight.AircraftType is not null)
        {
            aircraftType = new AircraftTypeSnapshot(flight.AircraftType.AircraftTypeId, flight.AircraftType.Model);
        }
        else
        {
            aircraftType = null;
        }

        var serviceLineResult = await inputBuilder.BuildServiceLinesAsync(request.ServiceLines, forceReturnToRamp: false, cancellationToken);
        if (serviceLineResult.IsFailure)
            return serviceLineResult.Error;

        var taskResult = await inputBuilder.BuildTasksAsync(request.Tasks, forceReturnToRamp: false, cancellationToken);
        if (taskResult.IsFailure)
            return taskResult.Error;

        var customer = flight.Customer with { };
        var station = flight.Station with { };
        var operation = flight.OperationType with { };

        var created = DomainWorkOrder.CreateWithFlight(
            flight.Id,
            customer,
            station,
            operation,
            flightNumber.Value,
            aircraftType,
            request.AircraftTailNumber,
            schedule.Value,
            serviceLines: serviceLineResult.Value!,
            tasks: taskResult.Value!,
            isCanceled: request.IsCanceled,
            cancellationAt: request.IsCanceled ? request.CancellationAt ?? DateTimeOffset.UtcNow : null,
            actualTime: actualTime,
            utcNow: DateTimeOffset.UtcNow,
            remarks: request.Remarks,
            createdByEmployeeId: request.CreatedByEmployeeId,
            customerSignature: request.CustomerSignature,
            clientMutationId: request.ClientMutationId);

        if (created.IsFailure)
            return created.Error;

        var workOrder = created.Value!;
        workOrders.Add(workOrder);

        var attach = flight.AttachWorkOrder(workOrder.Id);
        if (attach.IsFailure)
            return attach.Error;
        flights.Update(flight);

        // Broadcast a flight upsert so the originating device (and every other connected
        // device for assigned employees / the AOG / Ad Hoc station group) refreshes the
        // affected flight row — the new myWorkOrderId / status fields belong to the
        // flight projection. Threading the ClientMutationId as OriginMutationId lets the
        // originating device drop its optimistic outbox row the moment the echo arrives
        // (see SyncCoordinator.applyChange on mobile).
        FlightMobileSyncBroadcasts.EnqueueUpsert(
            mobileSync,
            flight,
            originMutationId: workOrder.ClientMutationId?.ToString());

        return new CreateWorkOrderForFlightResult(
            WorkOrderId: workOrder.Id.Value,
            FlightId: flight.Id.Value,
            Idempotent: false);
    }
}
