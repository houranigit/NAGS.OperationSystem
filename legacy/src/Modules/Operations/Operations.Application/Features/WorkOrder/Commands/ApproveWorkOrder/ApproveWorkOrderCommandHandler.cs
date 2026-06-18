using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;
using Operations.Domain.StationWorkOrderSequence;
using Operations.Domain.ValueObjects;
using DomainWorkOrder = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Features.WorkOrder.Commands.ApproveWorkOrder;

/// <summary>
/// Settles the flight from a single approved work order:
/// <list type="number">
///   <item>Pulls a fresh per-station sequence and formats the <see cref="WorkOrderNumber"/>.</item>
///   <item>Calls <see cref="DomainWorkOrder.Approve"/> (under-review → approved).</item>
///   <item>Calls <see cref="Flight.SetSettledWorkOrder"/> so the flight becomes Completed/Canceled and locked.</item>
///   <item>
///     Marks every other under-review work order on the same flight as <c>Deleting</c>;
///     the WorkOrderDeletionJob hard-deletes them after the configured grace period.
///     Already <c>Rejected</c> / <c>Deleting</c> siblings are left alone.
///   </item>
/// </list>
/// </summary>
public sealed class ApproveWorkOrderCommandHandler(
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IStationWorkOrderSequenceRepository sequences)
    : ICommandHandler<ApproveWorkOrderCommand>
{
    public async Task<Result> Handle(ApproveWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrderId = WorkOrderId.From(request.WorkOrderId);
        var workOrder = await workOrders.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        if (workOrder.Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Only an under-review work order can be approved.");

        if (workOrder.FlightId is null)
            return Error.Conflict("Work order is not linked to a flight.");

        var flight = await flights.GetByIdAsync(workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Linked flight not found.");

        if (flight.AcceptedWorkOrder is not null)
            return Error.Conflict("Flight already has an accepted work order. Revoke it before approving another.");

        var seq = await sequences.GetNextAsync(workOrder.Station.StationId, cancellationToken);
        var number = WorkOrderNumber.FromStationSequence(workOrder.Station.IataCode, seq);
        if (number.IsFailure)
            return number.Error;

        var now = DateTimeOffset.UtcNow;
        var approve = workOrder.Approve(number.Value, workOrder.FlightId, now);
        if (approve.IsFailure)
            return approve.Error;

        var snapshot = WorkOrderSnapshot.Create(workOrder.Id, number.Value);
        var settle = flight.SetSettledWorkOrder(snapshot, workOrder.IsCanceled, now);
        if (settle.IsFailure)
            return settle.Error;

        // Siblings still UnderReview enter the Deleting grace window. The deletion job
        // hard-deletes them after the configured delay; if this approval is revoked
        // first, RevokeWorkOrderCommandHandler restores them to UnderReview.
        var siblings = await workOrders.GetByFlightIdAsync(workOrder.FlightId, cancellationToken);
        foreach (var sib in siblings)
        {
            if (sib.Id == workOrder.Id || sib.Status != WorkOrderStatus.UnderReview)
                continue;
            var mark = sib.MarkForDeletion(now);
            if (mark.IsFailure)
                return mark.Error;
            workOrders.Update(sib);
        }

        workOrders.Update(workOrder);
        flights.Update(flight);
        return Result.Success();
    }
}
