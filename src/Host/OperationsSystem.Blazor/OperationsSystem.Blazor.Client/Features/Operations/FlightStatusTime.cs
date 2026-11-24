using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations;

internal static class FlightStatusTime
{
    private static readonly TimeSpan FullDateAge = TimeSpan.FromDays(7);

    public static string? Format(FlightListItem flight, DateTimeOffset now) => flight.Status switch
    {
        "OnGround" when flight.ActualArrivalUtc is { } ata && ata <= now =>
            now - ata >= FullDateAge
                ? UiStrings.Flights.OnGroundSince(Date(flight, ata))
                : UiStrings.Flights.OnGroundFor(Elapsed(now - ata)),
        "ArrivalNotRecorded" when flight.ScheduledArrivalUtc <= now =>
            AwaitingArrival(flight, now - flight.ScheduledArrivalUtc),
        "DueSoon" when flight.ScheduledArrivalUtc >= now =>
            UiStrings.Flights.StaIn($"{Math.Max(1, Math.Ceiling((flight.ScheduledArrivalUtc - now).TotalMinutes))}m"),
        _ => null
    };

    private static string AwaitingArrival(FlightListItem flight, TimeSpan elapsed)
    {
        if (elapsed >= FullDateAge)
            return UiStrings.Flights.StaOn(Date(flight, flight.ScheduledArrivalUtc));

        return elapsed < TimeSpan.FromMinutes(1)
            ? UiStrings.Flights.StaNow
            : UiStrings.Flights.StaAgo(Elapsed(elapsed));
    }

    private static string Date(FlightListItem flight, DateTimeOffset timestamp) =>
        StationTimeZone.FromId(flight.StationTimeZoneId).Format(timestamp, "MMM d, yyyy");

    private static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed.Days > 0)
            return elapsed.Hours > 0 ? $"{elapsed.Days}d {elapsed.Hours}h" : $"{elapsed.Days}d";

        if (elapsed.Hours > 0)
            return elapsed.Minutes > 0 ? $"{elapsed.Hours}h {elapsed.Minutes}m" : $"{elapsed.Hours}h";

        return elapsed.Minutes > 0 ? $"{elapsed.Minutes}m" : "<1m";
    }
}
