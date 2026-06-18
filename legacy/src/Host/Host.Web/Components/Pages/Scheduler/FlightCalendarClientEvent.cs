using System.Text.Json.Serialization;

namespace Host.Web.Components.Pages.Scheduler;

/// <summary>JSON shape returned to FullCalendar (camelCase for JS interop).</summary>
public sealed class FlightCalendarClientEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("start")]
    public string Start { get; init; } = "";

    [JsonPropertyName("end")]
    public string End { get; init; } = "";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; init; } = "transparent";

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; init; } = "#666";

    [JsonPropertyName("textColor")]
    public string TextColor { get; init; } = "#333";

    [JsonPropertyName("classNames")]
    public string[] ClassNames { get; init; } = ["ops-flight-cal-item"];

    /// <summary>
    /// Extra fields surfaced as <c>event.extendedProps</c> in FullCalendar — read by the
    /// flights bridge to format the in-cell label and the hover tooltip.
    /// </summary>
    [JsonPropertyName("extendedProps")]
    public FlightCalendarExtendedProps ExtendedProps { get; init; } = new();
}

/// <summary>
/// Domain context surfaced to the calendar so the JS bridge can render
/// "{CustomerCode}{FlightNumber} {Time}" in cells and the
/// "{...} on {StationName} - Contract {ContractNumber}" tooltip without another
/// round-trip to .NET.
/// </summary>
public sealed class FlightCalendarExtendedProps
{
    [JsonPropertyName("customerCode")]
    public string CustomerCode { get; init; } = "";

    [JsonPropertyName("flightNumber")]
    public string FlightNumber { get; init; } = "";

    [JsonPropertyName("stationName")]
    public string StationName { get; init; } = "";

    /// <summary>Snapshot of the resolved contract number — empty string for ad-hoc flights.</summary>
    [JsonPropertyName("contractNumber")]
    public string ContractNumber { get; init; } = "";
}
