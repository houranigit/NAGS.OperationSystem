using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrder.Commands.DeleteWorkOrder;

public sealed class DeleteWorkOrderCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders)
    : ICommandHandler<DeleteWorkOrderCommand>
{
    public async Task<Result> Handle(DeleteWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrderId = WorkOrderId.From(request.WorkOrderId);
        var workOrder = await workOrders.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        if (workOrder.Status == WorkOrderStatus.Approved)
            return Error.Conflict("Approved work orders must be revoked before they can be deleted.");

        if (workOrder.Status == WorkOrderStatus.Deleting)
            return Error.Conflict("This work order is pending automatic deletion. Revoke the related approval to restore it.");

        if (workOrder.FlightId is not null)
        {
            var flight = await flights.GetByIdAsync(workOrder.FlightId, cancellationToken);
            if (flight is not null)
            {
                // Best-effort detach — DetachWorkOrder fails if the link is missing, which is fine
                // (the work order may have only had FlightId set without an attachment row).
                _ = flight.DetachWorkOrder(workOrder.Id);
                flights.Update(flight);
            }
        }

        workOrders.Remove(workOrder);
        return Result.Success();
    }
}
