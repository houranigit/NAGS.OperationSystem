using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Domain.Flights;

namespace Operations.Application.Common;

/// <summary>Stages account history with the work mutation; the caller owns its single SaveChanges.</summary>
public static class FlightActivityRecorder
{
    public static async Task TouchAsync(IOperationsDbContext db, Flight flight, MobileFlightScopeContext actor,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var usedSearch = flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
            !flight.IsPerLanding && !flight.AssignedEmployees.Any(item => item.Employee.StaffMemberId == actor.StaffMemberId);
        var activity = db.FlightUserActivities.Local.SingleOrDefault(item => item.UserId == actor.UserId && item.FlightId == flight.Id)
            ?? await db.FlightUserActivities.SingleOrDefaultAsync(item => item.UserId == actor.UserId && item.FlightId == flight.Id, cancellationToken);
        if (activity is null)
            db.FlightUserActivities.Add(FlightUserActivity.Create(actor.UserId, flight.Id, occurredAtUtc, usedSearch));
        else activity.Touch(occurredAtUtc, usedSearch);
    }
}
