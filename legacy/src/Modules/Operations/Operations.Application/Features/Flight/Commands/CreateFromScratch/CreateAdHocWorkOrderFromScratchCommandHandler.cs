using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Readers;
using Core.Contracts.Seeding;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flight.Mobile;
using Operations.Application.Features.WorkOrder;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.ValueObjects;
using DomainFlight = Operations.Domain.Aggregates.Flight.Flight;
using DomainWorkOrder = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Features.Flight.Commands.CreateFromScratch;

/// <summary>
/// Mobile-only handler for the "create work order from scratch" flow. Resolves the AdHoc
/// operation type and the caller's employee snapshot, builds an ad-hoc <see cref="Flight"/>
/// and a <see cref="DomainWorkOrder"/> on it in one transaction, then attaches them
/// (which also moves the flight from <c>Scheduled</c> to <c>InProgress</c>).
/// </summary>
/// <remarks>
/// Idempotency: when <see cref="CreateAdHocWorkOrderFromScratchCommand.ClientFlightId"/>
/// or <see cref="CreateAdHocWorkOrderFromScratchCommand.ClientMutationId"/> is supplied
/// and matches an existing row, the handler returns the existing ids unchanged. This is
/// what lets the mobile outbox retry a queued submission safely even when the original
/// HTTP response was lost.
/// </remarks>
public sealed class CreateAdHocWorkOrderFromScratchCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IEmployeeReader employeeReader,
    IOperationTypeReader operationTypeReader,
    IAircraftTypeReader aircraftTypeReader,
    ICustomerReader customerReader,
    WorkOrderInputBuilder inputBuilder,
    IMobileSyncBroadcaster mobileSync,
    IOperationsDbContext db)
    : ICommandHandler<CreateAdHocWorkOrderFromScratchCommand, CreateAdHocFromScratchResult>
{
    public async Task<Result<CreateAdHocFromScratchResult>> Handle(
        CreateAdHocWorkOrderFromScratchCommand request,
        CancellationToken cancellationToken)
    {
        if (request.CreatorEmployeeId == Guid.Empty)
            return Error.Validation("Creator employee id is required.");

        if (request.CustomerId == Guid.Empty)
            return Error.Validation("Customer is required for an ad-hoc flight.");

        // Idempotency short-circuit. Two keys can match:
        //   • ClientMutationId → a previous submission produced a work order. The flight
        //     it belongs to is the row we want; nothing else changes.
        //   • ClientFlightId   → the flight is already on the server but the WO create
        //     half failed last time. We'd still need to create the WO, but in practice
        //     this is rare enough we return the existing ids and let the next mobile
        //     refresh reconcile — preferring "no duplicates ever" over "always retry the
        //     interrupted half". Either way the user sees their flight + WO on the next
        //     sync once the row eventually arrives.
        // Both lookups are by indexed column (filtered-unique on non-null), so the cost
        // is at most two seeks per request.
        if (request.ClientMutationId is { } mutationId && mutationId != Guid.Empty)
        {
            var existing = await db.WorkOrders
                .Where(w => w.ClientMutationId == mutationId)
                .Select(w => new { WoId = w.Id.Value, FlightId = w.FlightId == null ? (Guid?)null : w.FlightId.Value })
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is { FlightId: { } fid })
                return new CreateAdHocFromScratchResult(fid, existing.WoId, Idempotent: true);
        }

        if (request.ClientFlightId is { } clientFlightId && clientFlightId != Guid.Empty)
        {
            var existingFlight = await db.Flights
                .Where(f => f.ClientFlightId == clientFlightId)
                .Select(f => f.Id.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingFlight != Guid.Empty)
            {
                var existingWo = await db.WorkOrders
                    .Where(w => w.FlightId != null && w.FlightId.Value == existingFlight)
                    .Select(w => w.Id.Value)
                    .FirstOrDefaultAsync(cancellationToken);
                return new CreateAdHocFromScratchResult(
                    FlightId: existingFlight,
                    WorkOrderId: existingWo == Guid.Empty ? Guid.Empty : existingWo,
                    Idempotent: true);
            }
        }

        var creator = await employeeReader.GetByIdAsync(request.CreatorEmployeeId, cancellationToken);
        if (creator is null)
            return Error.NotFound("Creator employee not found.");

        var operationTypeRaw = await operationTypeReader.GetByIdAsync(CoreSeedIds.AdHocOperationType, cancellationToken);
        if (operationTypeRaw is null)
            return Error.Validation("Ad Hoc operation type is missing from the system.");
        var operationType = new OperationTypeSnapshot(operationTypeRaw.OperationTypeId, operationTypeRaw.Name);

        // Customer must exist and be active. We resolve a real CustomerSnapshot so the
        // flight column carries the airline's IATA + name (used by the flights grid)
        // instead of the old "AD / Ad Hoc" sentinel.
        var customerRaw = await customerReader.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customerRaw is null)
            return Error.NotFound("Customer not found.");
        if (!await customerReader.ExistsActiveAsync(request.CustomerId, cancellationToken))
            return Error.Validation("Customer is not active.");
        var customer = new Core.Contracts.Features.Customer.CustomerSnapshot(
            customerRaw.CustomerId,
            customerRaw.IataCode,
            customerRaw.Name);

        var flightNumberResult = FlightNumber.Create(request.FlightNumber);
        if (flightNumberResult.IsFailure)
            return flightNumberResult.Error;

        var schedule = ScheduledTime.Create(request.Sta, request.Std);
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

        AircraftTypeSnapshot? aircraftType = null;
        if (request.AircraftTypeId is { } aircraftTypeId)
        {
            var snap = await aircraftTypeReader.GetByIdAsync(aircraftTypeId, cancellationToken);
            if (snap is null)
                return Error.Validation("Aircraft type not found.");
            aircraftType = new AircraftTypeSnapshot(snap.AircraftTypeId, snap.Model);
        }

        if (creator.StationSnapshot is null)
            throw new InvalidOperationException("Creator employee station is missing.");
        // Ad-hoc flights have a real customer (so retroactive billing can attach them
        // to a contract) but no <c>ContractId</c> at create time. The station is forced
        // to the creator's station so a mobile user can never raise a work order at a
        // station they're not based at.

        var flight = DomainFlight.CreateAdHoc(
            customer,
            new Core.Contracts.Features.Station.StationSnapshot(
                creator.StationSnapshot.StationId,
                creator.StationSnapshot.Name,
                creator.StationSnapshot.IataCode),
            operationType,
            flightNumberResult.Value!,
            schedule.Value!,
            aircraftType,
            creator,
            DateTimeOffset.UtcNow,
            clientFlightId: request.ClientFlightId);
        if (flight.IsFailure)
            return flight.Error;

        var serviceLineResult = await inputBuilder.BuildServiceLinesAsync(request.ServiceLines, forceReturnToRamp: false, cancellationToken);
        if (serviceLineResult.IsFailure)
            return serviceLineResult.Error;

        var taskResult = await inputBuilder.BuildTasksAsync(request.Tasks, forceReturnToRamp: false, cancellationToken);
        if (taskResult.IsFailure)
            return taskResult.Error;

        var workOrderResult = DomainWorkOrder.CreateWithFlight(
            flight.Value!.Id,
            flight.Value.Customer with { },
            flight.Value.Station with { },
            flight.Value.OperationType with { },
            flightNumberResult.Value!,
            aircraftType,
            request.AircraftTailNumber,
            schedule.Value!,
            serviceLines: serviceLineResult.Value!,
            tasks: taskResult.Value!,
            isCanceled: request.IsCanceled,
            cancellationAt: request.IsCanceled ? request.CancellationAt ?? DateTimeOffset.UtcNow : null,
            actualTime: actualTime,
            utcNow: DateTimeOffset.UtcNow,
            remarks: request.Remarks,
            createdByEmployeeId: request.CreatorEmployeeId,
            customerSignature: request.CustomerSignature,
            clientMutationId: request.ClientMutationId);
        if (workOrderResult.IsFailure)
            return workOrderResult.Error;

        var workOrder = workOrderResult.Value!;
        var attach = flight.Value.AttachWorkOrder(workOrder.Id);
        if (attach.IsFailure)
            return attach.Error;

        flights.Add(flight.Value);
        workOrders.Add(workOrder);

        // Ad-hoc flights are created by the mobile client on the creator's own device.
        // The creator is the sole assignee → the flight appears on their "my flights"
        // immediately via the push, no AOG fan-out needed. Echoing the WO ClientMutationId
        // lets the originating device drop its optimistic outbox row the moment the push
        // arrives instead of waiting for the next periodic refresh.
        FlightMobileSyncBroadcasts.EnqueueUpsert(
            mobileSync,
            flight.Value,
            originMutationId: workOrder.ClientMutationId?.ToString());

        return new CreateAdHocFromScratchResult(
            FlightId: flight.Value.Id.Value,
            WorkOrderId: workOrder.Id.Value,
            Idempotent: false);
    }
}
