using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Common;
using Operations.Contracts;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.WorkOrders;

// --- Approve (settle flight + hand to billing) ------------------------------

public sealed record ApproveWorkOrderCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ApproveWorkOrderCommandValidator : AbstractValidator<ApproveWorkOrderCommand>
{
    public ApproveWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class ApproveWorkOrderCommandHandler(
    IOperationsDbContext db,
    IWorkOrderNumberAllocator allocator,
    IFlightTimelineWriter timeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ApproveWorkOrderCommand>
{
    public async Task<Result> Handle(ApproveWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var alreadyApproved = await db.WorkOrders.AnyAsync(
            w => w.FlightId == flight.Id && w.Id != workOrder.Id && w.Status == WorkOrderStatus.Approved, cancellationToken);
        if (alreadyApproved)
            return Error.Conflict("This flight already has an approved work order. Return it to review before approving another.", "Operations.WorkOrder.AlreadyApproved");

        var now = timeProvider.GetUtcNow();
        var sequence = await allocator.NextAsync(workOrder.Station.StationId, workOrder.Station.IataCode, cancellationToken);
        var number = WorkOrderNumber.FromStationSequence(workOrder.Station.IataCode, sequence);

        var approverId = user.UserId ?? Guid.Empty;
        var approve = workOrder.Approve(number, approverId, now);
        if (approve.IsFailure)
            return approve.Error;

        // Capture the approved work order's values + reference onto the flight (the billing-ready
        // source of truth); actual service lines/tasks stay on the locked approved work order.
        var settle = workOrder.IsCancellation
            ? flight.SettleCanceled(workOrder, now)
            : flight.SettleCompleted(workOrder, now);
        if (settle.IsFailure)
            return settle.Error;

        db.Enqueue(new FlightSentToBilling
        {
            FlightId = flight.Id,
            WorkOrderId = workOrder.Id,
            WorkOrderNumber = number.Value,
            Outcome = workOrder.IsCancellation ? "Canceled" : "Completed",
            CustomerId = flight.Customer.CustomerId,
            StationId = flight.Station.StationId,
            ApprovedByUserId = approverId,
            ActualFlightNumber = workOrder.FlightNumber.Value,
            ActualAircraftTypeId = workOrder.AircraftType?.AircraftTypeId,
            AircraftTailNumber = workOrder.AircraftTailNumber,
            ActualArrivalUtc = workOrder.Actuals?.Ata,
            ActualDepartureUtc = workOrder.Actuals?.Atd,
            CanceledAtUtc = workOrder.Cancellation?.CanceledAtUtc
        });

        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderApproved, now, workOrder.Id, number.Value, cancellationToken: cancellationToken);
        await timeline.AppendAsync(flight.Id,
            workOrder.IsCancellation ? FlightTimelineEventType.FlightCanceled : FlightTimelineEventType.FlightCompleted,
            now, workOrder.Id, number.Value, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder, WorkOrderTimelineEventType.Approved, now, number.Value, cancellationToken: cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

// --- Reject -----------------------------------------------------------------

public sealed record RejectWorkOrderCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class RejectWorkOrderCommandHandler(
    IOperationsDbContext db,
    IFlightTimelineWriter timeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    TimeProvider timeProvider) : ICommandHandler<RejectWorkOrderCommand>
{
    public async Task<Result> Handle(RejectWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var now = timeProvider.GetUtcNow();
        var wasApproved = workOrder.Status == WorkOrderStatus.Approved;
        var previousNumber = workOrder.Number?.Value;
        var ret = workOrder.ReturnToReview(now);
        if (ret.IsFailure)
            return ret.Error;

        if (wasApproved)
        {
            var clear = flight.ClearApprovedSnapshot(now);
            if (clear.IsFailure)
                return clear.Error;

            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.ApprovedSnapshotCleared, now, workOrder.Id, previousNumber, cancellationToken: cancellationToken);
        }
        else
        {
            flight.OnWorkOrderReturnedToReview(now);
        }

        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderReturned, now, workOrder.Id, previousNumber, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder, WorkOrderTimelineEventType.Returned, now, previousNumber, cancellationToken: cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

// --- Return approved/submitted work order to review (unlock) -----------------

public sealed record ReturnWorkOrderToReviewCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ReturnWorkOrderToReviewCommandHandler(
    IOperationsDbContext db,
    IFlightTimelineWriter timeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    TimeProvider timeProvider) : ICommandHandler<ReturnWorkOrderToReviewCommand>
{
    public async Task<Result> Handle(ReturnWorkOrderToReviewCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var now = timeProvider.GetUtcNow();
        var wasApproved = workOrder.Status == WorkOrderStatus.Approved;
        var previousNumber = workOrder.Number?.Value;

        // Returning wipes the number/approval metadata from the work order (a re-approval later gets
        // a fresh station-sequence number; the retired number stays visible in the timeline).
        var ret = workOrder.ReturnToReview(now);
        if (ret.IsFailure)
            return ret.Error;

        if (wasApproved)
        {
            // Clear the captured approved values + reference from the flight and revert it to InProgress.
            var clear = flight.ClearApprovedSnapshot(now);
            if (clear.IsFailure)
                return clear.Error;

            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.ApprovedSnapshotCleared, now, workOrder.Id, previousNumber, cancellationToken: cancellationToken);
        }
        else
        {
            flight.OnWorkOrderReturnedToReview(now);
        }

        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderReturned, now, workOrder.Id, previousNumber, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder, WorkOrderTimelineEventType.Returned, now, previousNumber, cancellationToken: cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}
