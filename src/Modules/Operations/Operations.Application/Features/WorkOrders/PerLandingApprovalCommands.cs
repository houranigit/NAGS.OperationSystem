using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Mobile;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.WorkOrders;

public sealed record PerLandingApprovalSelection(Guid FlightId, Guid WorkOrderId, byte[] RowVersion);

public sealed record ApprovePerLandingFlightsCommand(IReadOnlyList<PerLandingApprovalSelection> Selections)
    : ICommand<int>;

public sealed class ApprovePerLandingFlightsCommandValidator : AbstractValidator<ApprovePerLandingFlightsCommand>
{
    public ApprovePerLandingFlightsCommandValidator()
    {
        RuleFor(x => x.Selections).NotEmpty();
        RuleForEach(x => x.Selections).ChildRules(selection =>
        {
            selection.RuleFor(x => x.FlightId).NotEmpty();
            selection.RuleFor(x => x.WorkOrderId).NotEmpty();
            selection.RuleFor(x => x.RowVersion).NotEmpty();
        });
        RuleFor(x => x.Selections)
            .Must(items => items.Select(x => x.FlightId).Distinct().Count() == items.Count)
            .WithMessage("Each flight can be selected only once.");
    }
}

public sealed class ApprovePerLandingFlightsCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IWorkOrderTimelineWriter workOrderTimeline,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ApprovePerLandingFlightsCommand, int>
{
    public async Task<Result<int>> Handle(
        ApprovePerLandingFlightsCommand request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var workOrderIds = request.Selections.Select(x => x.WorkOrderId).ToList();
        var flightIds = request.Selections.Select(x => x.FlightId).ToList();

        var workOrders = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .Where(w => workOrderIds.Contains(w.Id))
            .ToListAsync(cancellationToken);
        var flights = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .Where(f => flightIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        if (workOrders.Count != request.Selections.Count || flights.Count != request.Selections.Count)
            return Error.NotFound("One or more selected Per Landing flights are no longer available.", "Operations.PerLanding.SelectionNotFound");

        var workOrdersById = workOrders.ToDictionary(w => w.Id);
        var flightsById = flights.ToDictionary(f => f.Id);
        foreach (var selection in request.Selections)
        {
            var workOrder = workOrdersById[selection.WorkOrderId];
            var flight = flightsById[selection.FlightId];

            if (workOrder.FlightId != flight.Id || workOrder.Type != WorkOrderType.Completion)
                return Ineligible();
            if (flight.Status != FlightStatus.InProgress || !flight.IsPerLanding)
                return Ineligible();

            var station = scopeResult.Value.EnsureStation(flight.Station.StationId);
            if (station.IsFailure)
                return station.Error;

            db.SetOriginalRowVersion(workOrder, selection.RowVersion);
        }

        var hasPerformedService = await db.WorkOrders.AsNoTracking()
            .QualifyingForOnCall()
            .AnyAsync(w => flightIds.Contains(w.FlightId), cancellationToken);
        if (hasPerformedService)
            return Ineligible();

        var alreadyApproved = await db.WorkOrders.AsNoTracking().AnyAsync(w =>
            flightIds.Contains(w.FlightId) && w.Status == WorkOrderStatus.Approved,
            cancellationToken);
        if (alreadyApproved)
            return Error.Conflict("One or more selected flights already has an approved work order.", "Operations.WorkOrder.FlightAlreadyApproved");

        var stations = workOrders
            .Select(w => w.Station)
            .GroupBy(s => s.StationId)
            .ToDictionary(group => group.Key, group => group.First());
        var stationIds = stations.Keys.ToList();
        var usedRows = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WorkOrderStatus.Approved &&
                stationIds.Contains(w.Station.StationId) &&
                w.ApprovalSequence != null)
            .Select(w => new { StationId = w.Station.StationId, Sequence = w.ApprovalSequence!.Value })
            .ToListAsync(cancellationToken);
        var usedByStation = stationIds.ToDictionary(
            stationId => stationId,
            stationId => usedRows.Where(row => row.StationId == stationId).Select(row => row.Sequence).ToHashSet());

        var now = timeProvider.GetUtcNow();
        var approverUserId = user.UserId ?? Guid.Empty;
        foreach (var selection in request.Selections)
        {
            var workOrder = workOrdersById[selection.WorkOrderId];
            var flight = flightsById[selection.FlightId];
            var used = usedByStation[workOrder.Station.StationId];
            var sequence = NextFreeSequence(used);
            var number = WorkOrderNumber.Format(workOrder.Station.IataCode, sequence);
            if (number.IsFailure)
                return number.Error;

            var approve = workOrder.ApprovePerLandingExtraction(
                sequence,
                number.Value,
                approverUserId,
                now);
            if (approve.IsFailure)
                return approve.Error;
            used.Add(sequence);

            var settle = flight.SettleCompleted(now);
            if (settle.IsFailure)
                return settle.Error;

            await workOrderTimeline.AppendAsync(
                workOrder.Id,
                WorkOrderTimelineEventType.Approved,
                now,
                details: workOrder.ApprovalNumber,
                cancellationToken: cancellationToken);
            await workOrderTimeline.AppendAsync(
                workOrder.Id,
                WorkOrderTimelineEventType.NumberAssigned,
                now,
                details: workOrder.ApprovalNumber,
                cancellationToken: cancellationToken);
            await flightTimeline.AppendAsync(
                flight.Id,
                FlightTimelineEventType.FlightCompleted,
                now,
                details: workOrder.ApprovalNumber,
                cancellationToken: cancellationToken);

            MobileFlightSync.EnqueueUpsert(mobileSync, flight);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            return Error.Conflict(
                "Per Landing approval conflicted with another update. Reload and try again.",
                "Operations.PerLanding.ApprovalConflict");
        }

        return request.Selections.Count;
    }

    private static int NextFreeSequence(HashSet<int> used)
    {
        var next = 1;
        while (used.Contains(next))
            next++;
        return next;
    }

    private static Error Ineligible() => Error.Conflict(
        "One or more selected flights is no longer eligible for Per Landing approval.",
        "Operations.PerLanding.Ineligible");
}
