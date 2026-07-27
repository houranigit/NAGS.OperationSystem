using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class DashboardPieChart
{
    private static readonly IReadOnlyList<string> DefaultFills =
    [
        "#8a1538", "#2f6fed", "#0f9f8f", "#f59e0b", "#7c3aed",
        "#e05263", "#0891b2", "#64748b", "#84a98c", "#d97706"
    ];

    private IReadOnlyList<PieEntry> entries = [];
    private IReadOnlyList<string> fills = DefaultFills;
    private string title = string.Empty;
    private string description = string.Empty;
    private string icon = "pie_chart";
    private string totalLabel = "Flights";
    private string flightsLabel = "flights";
    private string emptyText = "No matching flight data.";
    private string tone = "primary";
    private bool useCodeLabels = true;
    private IReadOnlyList<DashboardBreakdownItem>? projectedItems;
    private string projectionLabelKey = string.Empty;
    private long total;
    private bool hasData;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public string Description { get; set; } = string.Empty;
    [Parameter] public string Icon { get; set; } = "pie_chart";
    [Parameter, EditorRequired] public IReadOnlyList<DashboardBreakdownItem> Items { get; set; } = [];
    [Parameter] public string TotalLabel { get; set; } = "Flights";
    [Parameter] public string FlightsLabel { get; set; } = "flights";
    [Parameter] public string EmptyText { get; set; } = "No matching flight data.";
    [Parameter] public string OtherLabel { get; set; } = "Other";
    [Parameter] public string PerLandingLabel { get; set; } = "Per Landing";
    [Parameter] public string OnCallLabel { get; set; } = "On Call";
    [Parameter] public string Tone { get; set; } = "primary";
    [Parameter] public bool UseCodeLabels { get; set; } = true;
    [Parameter] public IReadOnlyList<string>? Fills { get; set; }

    private string CardClass => $"dpc-card dpc-card--{tone}";

    private string ChartAriaLabel =>
        $"{title}: {string.Join(", ", entries.Select(entry => $"{entry.Label} {entry.FlightCount} {FormatPercentage(entry.Percentage)}"))}";

    protected override void OnParametersSet()
    {
        title = Title;
        description = Description;
        icon = Icon;
        totalLabel = TotalLabel;
        flightsLabel = FlightsLabel;
        emptyText = EmptyText;
        tone = Tone;
        useCodeLabels = UseCodeLabels;
        fills = Fills is { Count: > 0 } ? Fills : DefaultFills;

        var labelKey = $"{OtherLabel}\u001f{PerLandingLabel}\u001f{OnCallLabel}\u001f{useCodeLabels}";
        if (!ReferenceEquals(projectedItems, Items) ||
            !string.Equals(projectionLabelKey, labelKey, StringComparison.Ordinal))
        {
            projectedItems = Items;
            projectionLabelKey = labelKey;
            entries = Items
                .Where(item => item.FlightCount > 0)
                .Select((item, index) =>
                {
                    var label = DisplayLabel(item);
                    return new PieEntry(
                        $"{item.Id?.ToString() ?? "other"}-{index}",
                        label,
                        item.FlightCount,
                        item.Percentage);
                })
                .ToList();

            total = entries.Sum(entry => entry.FlightCount);
            hasData = total > 0;
        }
    }

    private string DisplayLabel(DashboardBreakdownItem item)
    {
        if (item.IsOther)
            return OtherLabel;
        if (string.Equals(item.Label, "Per Landing", StringComparison.OrdinalIgnoreCase))
            return PerLandingLabel;
        if (string.Equals(item.Label, "On Call", StringComparison.OrdinalIgnoreCase))
            return OnCallLabel;

        return useCodeLabels && !string.IsNullOrWhiteSpace(item.Code)
            ? item.Code.Trim().ToUpperInvariant()
            : item.Label;
    }

    private static string FormatChartLabel(object value) =>
        Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static string FormatPercentage(double value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + "%";

    private string LegendSwatchFill(int index) =>
        fills.Count == 0
            ? "var(--os-color-primary)"
            : fills[index % fills.Count];

    private string LegendItemDescription(PieEntry entry) =>
        $"{entry.Label}: {FormatCount(entry.FlightCount)} {flightsLabel}, {FormatPercentage(entry.Percentage)}";

    private sealed record PieEntry(
        string Key,
        string Label,
        long FlightCount,
        double Percentage);
}
