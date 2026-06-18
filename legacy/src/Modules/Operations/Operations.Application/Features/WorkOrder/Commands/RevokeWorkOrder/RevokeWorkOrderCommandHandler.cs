using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrder.Commands.RevokeWorkOrder;

/// <summary>
/// Reverses an approval. The flight returns to <c>InProgress</c>, the revoked work order
/// goes back to <c>UnderReview</c>, and any sibling work orders that were marked
/// <c>Deleting</c> by the original approval are restored to <c>UnderReview</c>
/// (deletions that already ran are not reversible).
/// </summary>
public sealed class RevokeWorkOrderCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders)
    : ICommandHandler<RevokeWorkOrderCommand>
{
    public async Task<Result> Handle(RevokeWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrderId = WorkOrderId.From(request.WorkOrderId);
        var workOrder = await workOrders.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        if (workOrder.Status != WorkOrderStatus.Approved)
            return Error.Conflict("Only an approved work order can be revoked.");

        var revoked = workOrder.Revoke();
        if (revoked.IsFailure)
            return revoked;

        if (workOrder.FlightId is not null)
        {
            var flight = await flights.GetByIdAsync(workOrder.FlightId, cancellationToken);
            if (flight is null)
                return Error.NotFound("Linked flight not found.");

            var clear = flight.ClearAcceptedWorkOrderForRevoke();
            if (clear.IsFailure)
                return clear;

            flights.Update(flight);

            // Restore any siblings that were swept into Deleting by this approval.
            // Already-deleted rows simply won't be in this list; nothing to do.
            var siblings = await workOrders.GetByFlightIdAsync(workOrder.FlightId, cancellationToken);
            foreach (var sib in siblings)
            {
                if (sib.Id == workOrder.Id || sib.Status != WorkOrderStatus.Deleting)
                    continue;
                var restore = sib.RestoreFromDeletion();
                if (restore.IsFailure)
                    return restore;
                workOrders.Update(sib);
            }
        }

        workOrders.Update(workOrder);
        return Result.Success();
    }
}
