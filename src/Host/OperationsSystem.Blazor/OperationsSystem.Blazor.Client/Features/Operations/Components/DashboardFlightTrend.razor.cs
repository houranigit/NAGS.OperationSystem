using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class DashboardFlightTrend
{
    private IReadOnlyList<DashboardTimelinePoint> points = [];
    private IReadOnlyList<PresetOption> presets = [];
    private string title = string.Empty;
    private string description = string.Empty;
    private string rangeSummary = string.Empty;
    private string granularity = "Month";
    private string flightsLabel = "Flights";
    private string emptyText = "No flight data is available for this period.";
    private string zoomHint = "Scroll to zoom · drag to pan";
    private string periodLabel = "Period";
    private string rangeKey = string.Empty;
    private string previousRangeKey = string.Empty;
    private IReadOnlyList<DashboardTimelinePoint>? projectedPoints;
    private DashboardPeriodPreset selectedPreset;
    private DateTime trendMin;
    private DateTime trendMax;
    private double viewStart;
    private double viewEnd = 1;
    private bool busy;
    private bool hasData;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public string Description { get; set; } = string.Empty;
    [Parameter, EditorRequired] public IReadOnlyList<DashboardTimelinePoint> Points { get; set; } = [];
    [Parameter] public string Granularity { get; set; } = "Month";
    [Parameter] public string RangeSummary { get; set; } = string.Empty;
    [Parameter] public string RangeKey { get; set; } = string.Empty;
    [Parameter] public string FlightsLabel { get; set; } = "Flights";
    [Parameter] public string EmptyText { get; set; } = "No flight data is available for this period.";
    [Parameter] public string ZoomHint { get; set; } = "Scroll to zoom · drag to pan";
    [Parameter] public string PeriodLabel { get; set; } = "Period";
    [Parameter] public string TodayLabel { get; set; } = "Today";
    [Parameter] public string LastMonthLabel { get; set; } = "Last month";
    [Parameter] public string LastThreeMonthsLabel { get; set; } = "Last 3 months";
    [Parameter] public string MaxLabel { get; set; } = "Max";
    [Parameter] public DashboardPeriodPreset SelectedPreset { get; set; }
    [Parameter] public EventCallback<DashboardPeriodPreset> SelectedPresetChanged { get; set; }
    [Parameter] public bool Busy { get; set; }

    private TimeSpan TrendSpan => trendMax - trendMin;
    private int AxisTickDistance =>
        granularity == "Month" && TrendSpan <= TimeSpan.FromDays(62) ? 300 : 92;

    protected override void OnParametersSet()
    {
        title = Title;
        description = Description;
        if (!ReferenceEquals(projectedPoints, Points))
        {
            projectedPoints = Points;
            points = Points.OrderBy(point => point.BucketUtc).ToList();
            hasData = points.Any(point => point.FlightCount > 0);
            trendMin = points.Count == 0 ? DateTime.UtcNow.Date : points[0].BucketDateUtc;
            trendMax = points.Count == 0 ? DateTime.UtcNow.Date.AddDays(1) : points[^1].BucketDateUtc;
            if (trendMax <= trendMin)
                trendMax = trendMin.AddHours(1);
        }

        granularity = Granularity;
        rangeSummary = RangeSummary;
        rangeKey = RangeKey;
        if (!string.Equals(previousRangeKey, rangeKey, StringComparison.Ordinal))
        {
            previousRangeKey = rangeKey;
            viewStart = 0;
            viewEnd = 1;
        }
        flightsLabel = FlightsLabel;
        emptyText = EmptyText;
        zoomHint = ZoomHint;
        periodLabel = PeriodLabel;
        selectedPreset = SelectedPreset;
        busy = Busy;
        presets =
        [
            new(DashboardPeriodPreset.Today, TodayLabel),
            new(DashboardPeriodPreset.LastMonth, LastMonthLabel),
            new(DashboardPeriodPreset.LastThreeMonths, LastThreeMonthsLabel),
            new(DashboardPeriodPreset.Max, MaxLabel)
        ];
    }

    private Task SelectPresetAsync(DashboardPeriodPreset preset) =>
        SelectedPresetChanged.InvokeAsync(preset);

    private string PresetClass(DashboardPeriodPreset preset) =>
        preset == selectedPreset ? "dft-preset is-active" : "dft-preset";

    private string FormatAxisValue(object value) =>
        TryGetDate(value, out var date)
            ? granularity switch
            {
                "Hour" => date.ToString("HH:mm", CultureInfo.CurrentCulture),
                "Day" => date.ToString("dd MMM", CultureInfo.CurrentCulture),
                _ => date.ToString("MMM yy", CultureInfo.CurrentCulture)
            }
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    private string FormatTooltipDate(DateTimeOffset value) =>
        granularity switch
        {
            "Hour" => value.UtcDateTime.ToString("dd MMM yyyy · HH:mm 'UTC'", CultureInfo.CurrentCulture),
            "Day" => value.UtcDateTime.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture),
            _ => value.UtcDateTime.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
        };

    private DateTime ScaleToTrend(double position)
    {
        var fraction = Math.Clamp(position, 0, 1);
        return trendMin.AddTicks((long)(TrendSpan.Ticks * fraction));
    }

    private string FormatNavigatorDate(DateTime value) =>
        granularity switch
        {
            "Hour" => value.ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture),
            "Day" => value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
            _ => value.ToString("MMM yyyy", CultureInfo.CurrentCulture)
        };

    private static string FormatCountAxis(object value) =>
        Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static bool TryGetDate(object value, out DateTimeOffset date)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                date = offset;
                return true;
            case DateTime dateTime:
                date = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                return true;
            default:
                date = default;
                return false;
        }
    }

    private sealed record PresetOption(DashboardPeriodPreset Value, string Label);
}
