using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Readers;
using Operations.Application.Features.Flight.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.WorkOrder.Commands.UpdateWorkOrder;

/// <summary>
/// Applies an edit to an under-review work order. Customer / station / operation type /
/// schedule keep tracking the linked flight (refreshed on save). Lines and tasks are
/// full-sync — the supplied collections replace the existing rows.
/// </summary>
public sealed class UpdateWorkOrderCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IAircraftTypeReader aircraftTypeReader,
    Operations.Application.Features.WorkOrder.WorkOrderInputBuilder inputBuilder,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateWorkOrderCommand>
{
    public async Task<Result> Handle(UpdateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrder = await workOrders.GetByIdAsync(WorkOrderId.From(request.WorkOrderId), cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        if (workOrder.Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Only an under-review work order can be edited.");

        Operations.Domain.Aggregates.Flight.Flight? flight = null;
        if (workOrder.FlightId is not null)
        {
            flight = await flights.GetByIdAsync(workOrder.FlightId, cancellationToken);
            if (flight is null)
                return Error.NotFound("Linked flight not found.");
        }

        var flightNumber = FlightNumber.Create(request.FlightNumber);
        if (flightNumber.IsFailure)
            return flightNumber.Error;

        ScheduledTime schedule;
        if (flight is not null)
        {
            var s = ScheduledTime.Create(flight.Schedule.Sta, flight.Schedule.Std);
            if (s.IsFailure)
                return s.Error;
            schedule = s.Value!;
        }
        else
        {
            schedule = workOrder.Schedule;
        }

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

        var customer = flight is not null ? flight.Customer with { } : workOrder.Customer with { };
        var station = flight is not null ? flight.Station with { } : workOrder.Station with { };
        var operation = flight is not null ? flight.OperationType with { } : workOrder.OperationType with { };

        var apply = workOrder.UpdateBasicInfo(
            customer,
            station,
            operation,
            flightNumber.Value,
            aircraftType,
            request.AircraftTailNumber,
            schedule,
            serviceLineResult.Value!,
            taskResult.Value!,
            request.IsCanceled,
            request.IsCanceled ? request.CancellationAt ?? DateTimeOffset.UtcNow : null,
            actualTime,
            request.Remarks,
            customerSignature: request.CustomerSignature);

        if (apply.IsFailure)
            return apply;

        workOrders.Update(workOrder);

        if (flight is not null)
        {
            var bump = flight.NotifyLinkedWorkOrderChanged();
            if (bump.IsFailure)
                return bump;
            flights.Update(flight);

            FlightMobileSyncBroadcasts.EnqueueUpsert(
                mobileSync,
                flight,
                originMutationId: request.ClientMutationId?.ToString());
        }

        return Result.Success();
    }
}
