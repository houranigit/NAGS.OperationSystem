using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;

namespace OperationsSystem.Blazor.Client.Features.Operations;

internal sealed record FlightStatusOption(string Label, string Value, string Tone)
{
    public static FlightStatusOption[] CreateOptions() =>
    [
        new("Scheduled", "Scheduled", "gray"),
        new("Due soon", "DueSoon", "warning"),
        new(UiStrings.Flights.AwaitingAta, "ArrivalNotRecorded", "orange"),
        new("On ground", "OnGround", "info"),
        new("RTR", "ReturnedToRamp", "purple"),
        new("Departed", "Departed", "slate"),
        new(UiStrings.Flights.AutoClosed, FlightListItem.AutoClosedStatus, "slate"),
        new("Canceled", "Canceled", "danger"),
        new("Conflict", "Conflict", "critical")
    ];
}
