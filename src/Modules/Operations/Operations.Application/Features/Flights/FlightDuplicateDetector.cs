using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flights;

/// <summary>
/// Exact same-flight detection. A duplicate flight is another non-terminal flight with the same
/// customer, station, STA, and STD.
/// </summary>
public sealed class FlightDuplicateDetector(IOperationsDbContext db)
{
    public const int StrongMatchThreshold = 100;

    public async Task<IReadOnlyList<DuplicateCandidateDto>> FindAsync(
        Guid customerId,
        Guid stationId,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset scheduledDepartureUtc,
        Guid? excludeFlightId,
        CancellationToken cancellationToken)
    {
        return await db.Flights.AsNoTracking()
            .Where(f => f.Status != FlightStatus.Merged && f.Status != FlightStatus.Canceled)
            .Where(f => excludeFlightId == null || f.Id != excludeFlightId)
            .Where(f => f.Customer.CustomerId == customerId)
            .Where(f => f.Station.StationId == stationId)
            .Where(f => f.Schedule.Sta == scheduledArrivalUtc)
            .Where(f => f.Schedule.Std == scheduledDepartureUtc)
            .OrderBy(f => f.FlightNumber.Value)
            .ThenBy(f => f.Id)
            .Select(f => new DuplicateCandidateDto(
                f.Id,
                f.FlightNumber.Value,
                f.Customer.Name,
                f.Station.IataCode,
                f.Schedule.Sta,
                StrongMatchThreshold))
            .ToListAsync(cancellationToken);
    }
}
