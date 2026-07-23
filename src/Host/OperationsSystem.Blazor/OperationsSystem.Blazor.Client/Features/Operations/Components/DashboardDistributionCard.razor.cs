using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class DashboardDistributionCard
{
    private const int MaximumItems = 6;

    private IReadOnlyList<DistributionEntry> entries = [];
    private IReadOnlyList<string> chartFills = [];
    private IReadOnlyList<string> chartStrokes = [];
    private long displayedTotal;

    private static readonly string[] Palette =
    [
        "var(--os-color-primary)",
        "var(--os-color-info)",
        "var(--os-color-success)",
        "var(--os-color-warning)",
        "var(--os-color-danger)",
        "var(--os-color-text-muted)"
    ];

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Description { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Icon { get; set; } = string.Empty;
    [Parameter, EditorRequired] public IReadOnlyList<DashboardBreakdownItem> Items { get; set; } = [];
    [Parameter] public long Total { get; set; }
    [Parameter] public bool UseBars { get; set; }
    [Parameter] public string EmptyTitle { get; set; } = "No flight data";
    [Parameter] public string EmptyText { get; set; } = "No activity matches the selected dashboard filters.";
    [Parameter] public string TotalLabel { get; set; } = "Total flights";
    [Parameter] public string OtherLabel { get; set; } = "Other";
    [Parameter] public string GroupedLabelFormat { get; set; } = "{0} grouped";
    [Parameter] public string FlightsLabel { get; set; } = "flights";

    private bool HasData => displayedTotal > 0 && entries.Count > 0;

    private string ChartAriaLabel =>
        $"{Title}: {FormatCount(displayedTotal)} {TotalLabel}";

    protected override void OnParametersSet()
    {
        displayedTotal = Math.Max(0, Total);

        var source = (Items ?? [])
            .Where(item => item.FlightCount > 0)
            .Take(MaximumItems)
            .ToArray();

        var result = new List<DistributionEntry>(source.Length);

        for (var index = 0; index < source.Length; index++)
        {
            var item = source[index];
            var percentage = ClampPercentage(item.Percentage);
            var key = item.Id?.ToString("N", CultureInfo.InvariantCulture)
                      ?? $"{item.Label}:{item.Code}:{index}";

            result.Add(new DistributionEntry(
                key,
                DisplayLabel(item),
                item.FlightCount,
                percentage,
                PaletteClass(index)));
        }

        entries = result;
        chartFills = Palette.Take(result.Count).ToArray();
        chartStrokes = Enumerable.Repeat("var(--os-color-surface)", result.Count).ToArray();
    }

    private static double ClampPercentage(double percentage) =>
        double.IsFinite(percentage) ? Math.Clamp(percentage, 0d, 100d) : 0d;

    private static string PaletteClass(int index) => $"odc-palette-{index % MaximumItems}";

    private static string FormatCount(long count) =>
        count.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatPercentage(double percentage) =>
        $"{percentage.ToString("0.#", CultureInfo.CurrentCulture)}%";

    private static string SvgNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private string DisplayLabel(DashboardBreakdownItem item) =>
        item.IsOther ? OtherLabel : item.Label;

    private string FormatChartLabel(object value)
    {
        var count = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var percentage = displayedTotal > 0
            ? Math.Clamp(count / displayedTotal * 100d, 0d, 100d)
            : 0d;

        return $"{FormatCount(Convert.ToInt64(count))} · {FormatPercentage(percentage)}";
    }

    private sealed record DistributionEntry(
        string Key,
        string Label,
        long FlightCount,
        double Percentage,
        string PaletteClass);
}
