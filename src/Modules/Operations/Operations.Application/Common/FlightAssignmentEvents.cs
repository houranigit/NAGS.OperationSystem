using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using Operations.Application.Abstractions;
using Operations.Domain.Flights;

namespace Operations.Application.Common;

/// <summary>Writes new-roster assignment facts to the Operations outbox inside the flight transaction.</summary>
internal static class FlightAssignmentEvents
{
    public static void Enqueue(
        IOperationsDbContext db,
        Flight flight,
        IEnumerable<Guid> newlyAssignedStaffMemberIds,
        IUserContext actor,
        IAuditContext auditContext,
        DateTimeOffset occurredOnUtc,
        global::Operations.Contracts.FlightAssignmentSource source =
            global::Operations.Contracts.FlightAssignmentSource.Roster)
    {
        foreach (var staffMemberId in newlyAssignedStaffMemberIds.Distinct())
        {
            db.Enqueue(new global::Operations.Contracts.FlightEmployeeAssigned
            {
                FlightId = flight.Id,
                FlightNumber = flight.FlightNumber.Value,
                StaffMemberId = staffMemberId,
                AssignedByUserId = actor.UserId,
                AssignedByStaffMemberId = actor.ExternalReferenceId,
                AssignedByDisplayName = auditContext.ActorDisplayName,
                Source = source,
                OccurredOnUtc = occurredOnUtc
            });
        }
    }
}
