using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Mobile;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flights;

// --- Claim a Per-Landing flight ---------------------------------------------

public sealed record ClaimPerLandingFlightCommand(Guid FlightId, byte[] RowVersion) : ICommand;

public sealed class ClaimPerLandingFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    IMobileSyncBroadcaster mobileSync,
    TimeProvider timeProvider) : ICommandHandler<ClaimPerLandingFlightCommand>
{
    public async Task<Result> Handle(ClaimPerLandingFlightCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.Include(f => f.AssignedEmployees).Include(f => f.PlannedServices)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;
        if (scopeResult.Value.StaffMemberId is not { } staffId)
            return Error.Forbidden("Only station staff can claim a flight.", "Operations.Flight.ClaimNotAllowed");

        var employee = await resolver.StaffMemberAsync(staffId, cancellationToken);
        if (employee.IsFailure)
            return employee.Error;

        var now = timeProvider.GetUtcNow();
        var alreadyAssigned = flight.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId);
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var claim = flight.Claim(employee.Value, now);
        if (claim.IsFailure)
            return claim.Error;

        if (!alreadyAssigned)
            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.Value.FullName, cancellationToken: cancellationToken);

        MobileFlightSync.EnqueueUpsert(mobileSync, flight);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
