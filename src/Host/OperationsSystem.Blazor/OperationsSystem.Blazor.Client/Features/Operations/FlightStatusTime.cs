using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations;

internal static class FlightStatusTime
{
    private static readonly TimeSpan FullDateAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan DueSoonWindow = TimeSpan.FromMinutes(30);

    public static (string? Status, string? Text) At(FlightListItem flight, DateTimeOffset now)
    {
        var untilSta = flight.ScheduledArrivalUtc - now;
        var status = flight.Status is "Scheduled" or "DueSoon" or "ArrivalNotRecorded"
            ? ScheduleStatus(untilSta)
            : flight.Status;
        var text = status switch
        {
            "OnGround" when flight.ActualArrivalUtc is { } ata && ata <= now =>
                now - ata >= FullDateAge
                    ? UiStrings.Flights.OnGroundSince(Date(flight, ata))
                    : UiStrings.Flights.OnGroundFor(Elapsed(now - ata)),
            "ReturnedToRamp" when flight.ReturnedToRampUtc is { } rtr && rtr <= now =>
                now - rtr >= FullDateAge
                    ? UiStrings.Flights.RtrSince(Date(flight, rtr))
                    : UiStrings.Flights.RtrFor(Elapsed(now - rtr)),
            "ArrivalNotRecorded" => AwaitingArrival(flight, -untilSta),
            "DueSoon" => UiStrings.Flights.StaIn($"{Math.Ceiling(untilSta.TotalMinutes)}m"),
            _ => null
        };
        return (status, text);
    }

    private static string ScheduleStatus(TimeSpan untilSta)
    {
        if (untilSta <= TimeSpan.Zero)
            return "ArrivalNotRecorded";

        return untilSta <= DueSoonWindow ? "DueSoon" : "Scheduled";
    }

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

    internal static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed.Days > 0)
            return elapsed.Hours > 0 ? $"{elapsed.Days}d {elapsed.Hours}h" : $"{elapsed.Days}d";

        if (elapsed.Hours > 0)
            return elapsed.Minutes > 0 ? $"{elapsed.Hours}h {elapsed.Minutes}m" : $"{elapsed.Hours}h";

        return elapsed.Minutes > 0 ? $"{elapsed.Minutes}m" : "<1m";
    }
}
