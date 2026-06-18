using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Operations.Application.Features.Flight.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrder.Commands.RecordReturnToRampLines;

/// <summary>
/// Loads the work order, builds the inputs (forcing <c>ReturnToRamp = true</c>) and asks
/// the aggregate to append them. Mirrors the line-resolution pattern from
/// <see cref="UpdateWorkOrder.UpdateWorkOrderCommandHandler"/>: service rows resolve their
/// service+employee snapshots through Core readers; task rows resolve participating
/// employees plus optional Store items.
/// </summary>
public sealed class RecordReturnToRampLinesCommandHandler(
    IWorkOrderRepository workOrders,
    IFlightRepository flights,
    Operations.Application.Features.WorkOrder.WorkOrderInputBuilder inputBuilder,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<RecordReturnToRampLinesCommand>
{
    public async Task<Result> Handle(RecordReturnToRampLinesCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrder = await workOrders.GetByIdAsync(WorkOrderId.From(request.WorkOrderId), cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        if (workOrder.Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Return-to-ramp lines can only be added while the work order is under review.");

        var serviceLineResult = await inputBuilder.BuildServiceLinesAsync(request.ServiceLines, forceReturnToRamp: true, cancellationToken);
        if (serviceLineResult.IsFailure)
            return serviceLineResult.Error;

        var taskResult = await inputBuilder.BuildTasksAsync(request.Tasks, forceReturnToRamp: true, cancellationToken);
        if (taskResult.IsFailure)
            return taskResult.Error;

        var apply = workOrder.AppendReturnToRampLines(
            serviceLineResult.Value!,
            taskResult.Value!,
            customerSignature: request.CustomerSignature);

        if (apply.IsFailure)
            return apply;

        workOrders.Update(workOrder);

        if (workOrder.FlightId is not null)
        {
            var flight = await flights.GetByIdAsync(workOrder.FlightId, cancellationToken);
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
        }

        return Result.Success();
    }
}
