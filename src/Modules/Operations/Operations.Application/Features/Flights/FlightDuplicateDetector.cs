using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flights;

/// <summary>
/// Probabilistic same-flight detection for ad-hoc creation. Scores candidates primarily on
/// customer + station + scheduled-time proximity, with flight number as a supporting signal.
/// Deterministic duplicate work orders (a flight with multiple active work orders) are handled elsewhere.
/// </summary>
public sealed class FlightDuplicateDetector(IOperationsDbContext db)
{
    public const int StrongMatchThreshold = 70;
    private static readonly TimeSpan Window = TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<DuplicateCandidateDto>> FindAsync(
        Guid customerId,
        Guid stationId,
        string flightNumber,
        DateTimeOffset scheduledArrivalUtc,
        CancellationToken cancellationToken)
    {
        var from = scheduledArrivalUtc - Window;
        var to = scheduledArrivalUtc + Window;
        var normalizedNumber = flightNumber.Trim().ToUpperInvariant();

        var nearby = await db.Flights.AsNoTracking()
            .Where(f => f.Status != FlightStatus.Merged && f.Status != FlightStatus.Canceled)
            .Where(f => f.Station.StationId == stationId)
            .Where(f => f.Schedule.Sta >= from && f.Schedule.Sta <= to)
            .Select(f => new
            {
                f.Id,
                Number = f.FlightNumber.Value,
                CustomerId = f.Customer.CustomerId,
                CustomerName = f.Customer.Name,
                StationIata = f.Station.IataCode,
                Sta = f.Schedule.Sta
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<DuplicateCandidateDto>();
        foreach (var f in nearby)
        {
            var score = 0;

            // Station already matches (all rows share the station): base weight.
            score += 25;

            if (f.CustomerId == customerId)
                score += 30;

            var minutesApart = Math.Abs((f.Sta - scheduledArrivalUtc).TotalMinutes);
            if (minutesApart <= 30)
                score += 30;
            else if (minutesApart <= 120)
                score += 20;
            else if (minutesApart <= 360)
                score += 10;

            if (string.Equals(f.Number, normalizedNumber, StringComparison.OrdinalIgnoreCase))
                score += 15;

            if (score >= 40)
                candidates.Add(new DuplicateCandidateDto(f.Id, f.Number, f.CustomerName, f.StationIata, f.Sta, score));
        }

        return candidates.OrderByDescending(c => c.Score).ToList();
    }
}
