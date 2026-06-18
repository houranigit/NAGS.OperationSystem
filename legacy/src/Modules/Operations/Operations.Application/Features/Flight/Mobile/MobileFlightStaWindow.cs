using FlightRoot = Operations.Domain.Aggregates.Flight.Flight;

namespace Operations.Application.Features.Flight.Mobile;

/// <summary>
/// Shared ±STA window filter for mobile flight list queries.
/// </summary>
internal static class MobileFlightStaWindow
{
    /// <summary>
    /// Keeps flights whose STA falls within ±<paramref name="windowHours"/> of
    /// <paramref name="nowUtc"/>.
    /// </summary>
    public static IQueryable<FlightRoot> WhereStaWithinWindow(
        this IQueryable<FlightRoot> query,
        DateTimeOffset nowUtc,
        int windowHours)
    {
        var hours = Math.Clamp(windowHours, 1, 168);
        var windowStart = nowUtc.AddHours(-hours);
        var windowEnd = nowUtc.AddHours(hours);
        return query.Where(f => f.Schedule.Sta >= windowStart && f.Schedule.Sta <= windowEnd);
    }
}
