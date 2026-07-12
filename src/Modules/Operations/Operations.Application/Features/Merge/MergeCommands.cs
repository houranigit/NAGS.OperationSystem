using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Mobile;

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

public sealed class MergeDuplicateFlightsCommandHandler(
    IOperationsDbContext db,
    IMobileSyncBroadcaster mobileSync,
    TimeProvider timeProvider)
    : ICommandHandler<MergeDuplicateFlightsCommand>
{
    public async Task<Result> Handle(MergeDuplicateFlightsCommand request, CancellationToken cancellationToken)
    {
        var survivor = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.SurvivorFlightId, cancellationToken);
        if (survivor is null)
            return Error.NotFound("Surviving flight not found.", "Operations.Flight.NotFound");

        var loser = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.LoserFlightId, cancellationToken);
        if (loser is null)
            return Error.NotFound("Losing flight not found.", "Operations.Flight.NotFound");

        var now = timeProvider.GetUtcNow();
        var merge = loser.MarkMergedInto(survivor.Id, now);
        if (merge.IsFailure)
            return merge.Error;

        // The merged (loser) flight must vanish from every mobile cache; the survivor may have
        // absorbed edits, so refresh it.
        MobileFlightSync.EnqueueDelete(mobileSync, loser);
        MobileFlightSync.EnqueueUpsert(mobileSync, survivor);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
