using Microsoft.EntityFrameworkCore;
using Operations.Contracts.Readers;
using Operations.Domain.Enumerations;
using Operations.Infrastructure.Persistence;

namespace Operations.Infrastructure.Readers;

/// <summary>
/// Validates the immutable reminder snapshot against the live Operations aggregate without
/// exposing Operations persistence or domain types to the Notifications module.
/// </summary>
public sealed class FlightReminderEligibilityReader(OperationsDbContext db) : IFlightReminderEligibilityReader
{
    public Task<bool> IsEligibleAsync(
        Guid flightId,
        Guid staffMemberId,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        scheduledArrivalUtc = scheduledArrivalUtc.ToUniversalTime();
        evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();

        return db.Flights.AsNoTracking().AnyAsync(flight =>
                flight.Id == flightId &&
                (flight.Status == FlightStatus.Scheduled || flight.Status == FlightStatus.InProgress) &&
                flight.Schedule.Sta == scheduledArrivalUtc &&
                flight.Schedule.Sta > evaluatedAtUtc &&
                flight.AssignedEmployees.Any(assignment =>
                    assignment.Employee.StaffMemberId == staffMemberId),
            cancellationToken);
    }
}
