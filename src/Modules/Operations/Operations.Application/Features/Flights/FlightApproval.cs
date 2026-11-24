using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Common;

namespace Operations.Application.Features.Flights;

public sealed record SetFlightApprovalCommand(Guid FlightId, bool Approve, byte[] RowVersion) : ICommand;

public sealed class SetFlightApprovalCommandHandler(IOperationsDbContext db, PortalFlightScope scope,
    IUserDisplayNameReader userNames, TimeProvider clock, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<SetFlightApprovalCommand>
{
    public async Task<Result> Handle(SetFlightApprovalCommand request, CancellationToken cancellationToken)
    {
        if (request.RowVersion is not { Length: 8 })
            return ConcurrencyErrors.PreconditionRequired;

        var access = await scope.ResolveApprovalAsync(cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var flight = await FlightServiceWrites.Load(access.Value.Apply(db.Flights))
            .SingleOrDefaultAsync(item => item.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (!flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;

        var now = clock.GetUtcNow();
        Result change;
        if (request.Approve)
        {
            var reviewerId = access.Value.UserId;
            var names = await FlightWorkAttribution.NamesAsync(userNames, [reviewerId], cancellationToken);
            if (names.GetValueOrDefault(reviewerId) is not { Length: > 0 } reviewerName)
                return Error.Failure("The reviewer name could not be loaded. Please try again.", "Operations.Flight.ReviewerUnavailable");

            change = flight.Approve(reviewerId, reviewerName, now);
        }
        else
        {
            change = flight.Unlock(now);
        }

        if (change.IsFailure)
            return change.Error;

        db.SetOriginalRowVersion(flight, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        FlightServiceWrites.Notify(mobileSync, clock);
        return Result.Success();
    }
}
