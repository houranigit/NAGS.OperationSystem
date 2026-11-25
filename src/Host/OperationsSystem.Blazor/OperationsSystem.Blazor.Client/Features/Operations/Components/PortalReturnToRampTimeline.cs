using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed record PortalReturnToRampTimeline(
    DateTimeOffset? FlightDeparture, int? PreviousSequence, DateTimeOffset? PreviousDeparture,
    int? NextSequence, DateTimeOffset? NextArrival)
{
    internal static PortalReturnToRampTimeline ForOccurrence(PortalFlightOperations flight, int sequence,
        DateTimeOffset? flightDeparture, IEnumerable<PortalReturnToRampDraft> drafts)
    {
        var windows = flight.ReturnToRamps.Select(item => new Window(item.Sequence, item.ArrivedAtUtc, item.DepartedAtUtc))
            .Concat(drafts.Select(item => new Window(item.Sequence, item.Request.Arrival.ArrivedAtUtc,
                item.Request.Departure?.DepartedAtUtc)))
            .ToArray();
        var earlier = windows.Where(item => item.Sequence < sequence);
        var previous = earlier.FirstOrDefault(item => item.Departure is null) ?? earlier.MaxBy(item => item.Departure);
        var next = windows.Where(item => item.Sequence > sequence).MinBy(item => item.Arrival);
        return new(flightDeparture, previous?.Sequence, previous?.Departure, next?.Sequence, next?.Arrival);
    }

    internal static DateTimeOffset? LatestFlightDeparture(PortalFlightOperations flight,
        Guid? replacedId = null, DateTimeOffset? replacement = null) =>
        flight.DepartureSubmissions
            .Where(item => item.Id != replacedId)
            .Select(item => (DateTimeOffset?)item.Atd)
            .Append(replacement)
            .Max();

    private sealed record Window(int Sequence, DateTimeOffset Arrival, DateTimeOffset? Departure);
}
