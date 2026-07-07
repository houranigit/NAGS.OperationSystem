using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Common;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Merge;

// The survivor is edited in place by the admin through the normal update endpoints before/after this
// call; merge collapses the duplicate by soft-archiving the loser and linking it to the survivor.

// --- Merge duplicate ad-hoc flights -----------------------------------------

public sealed record MergeDuplicateFlightsCommand(Guid SurvivorFlightId, Guid LoserFlightId) : ICommand;

public sealed class MergeDuplicateFlightsCommandValidator : AbstractValidator<MergeDuplicateFlightsCommand>
{
    public MergeDuplicateFlightsCommandValidator()
    {
        RuleFor(x => x.SurvivorFlightId).NotEmpty();
        RuleFor(x => x.LoserFlightId).NotEmpty().NotEqual(x => x.SurvivorFlightId);
    }
}

public sealed class MergeDuplicateFlightsCommandHandler(IOperationsDbContext db, TimeProvider timeProvider)
    : ICommandHandler<MergeDuplicateFlightsCommand>
{
    public async Task<Result> Handle(MergeDuplicateFlightsCommand request, CancellationToken cancellationToken)
    {
        var survivor = await db.Flights.FirstOrDefaultAsync(f => f.Id == request.SurvivorFlightId, cancellationToken);
        if (survivor is null)
            return Error.NotFound("Surviving flight not found.", "Operations.Flight.NotFound");

        var loser = await db.Flights.FirstOrDefaultAsync(f => f.Id == request.LoserFlightId, cancellationToken);
        if (loser is null)
            return Error.NotFound("Losing flight not found.", "Operations.Flight.NotFound");

        var now = timeProvider.GetUtcNow();
        var merge = loser.MarkMergedInto(survivor.Id, now);
        if (merge.IsFailure)
            return merge.Error;

        // Re-point the loser's non-terminal work orders to the survivor; duplicates among them are then
        // resolved via the work-order merge flow.
        var loserWorkOrders = await db.WorkOrders
            .Where(w => w.FlightId == loser.Id && w.Status != WorkOrderStatus.Approved && w.SupersededByWorkOrderId == null)
            .ToListAsync(cancellationToken);
        foreach (var workOrder in loserWorkOrders)
            workOrder.ReassignToFlight(survivor.Id, now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Merge duplicate work orders (same flight) ------------------------------

public sealed record MergeDuplicateWorkOrdersCommand(Guid SurvivorWorkOrderId, Guid LoserWorkOrderId) : ICommand;

public sealed class MergeDuplicateWorkOrdersCommandValidator : AbstractValidator<MergeDuplicateWorkOrdersCommand>
{
    public MergeDuplicateWorkOrdersCommandValidator()
    {
        RuleFor(x => x.SurvivorWorkOrderId).NotEmpty();
        RuleFor(x => x.LoserWorkOrderId).NotEmpty().NotEqual(x => x.SurvivorWorkOrderId);
    }
}

public sealed class MergeDuplicateWorkOrdersCommandHandler(
    IOperationsDbContext db,
    IWorkOrderTimelineWriter workOrderTimeline,
    TimeProvider timeProvider)
    : ICommandHandler<MergeDuplicateWorkOrdersCommand>
{
    public async Task<Result> Handle(MergeDuplicateWorkOrdersCommand request, CancellationToken cancellationToken)
    {
        var survivor = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.SurvivorWorkOrderId, cancellationToken);
        if (survivor is null)
            return Error.NotFound("Surviving work order not found.", "Operations.WorkOrder.NotFound");

        var loser = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.LoserWorkOrderId, cancellationToken);
        if (loser is null)
            return Error.NotFound("Losing work order not found.", "Operations.WorkOrder.NotFound");

        if (survivor.FlightId != loser.FlightId)
            return Error.Validation("Both work orders must belong to the same flight.", "Operations.WorkOrder.MergeDifferentFlights");

        var now = timeProvider.GetUtcNow();
        var supersede = loser.Supersede(survivor.Id, now);
        if (supersede.IsFailure)
            return supersede.Error;

        await workOrderTimeline.AppendAsync(
            loser,
            WorkOrderTimelineEventType.Superseded,
            now,
            loser.Number?.Value,
            $"Merged into {survivor.Number?.Value ?? survivor.Id.ToString()[..8]}.",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
