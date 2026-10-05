using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class DashboardFlightTrend
{
    private IReadOnlyList<ChartPoint> points = [];
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
    private string projectedGranularity = string.Empty;
    private string projectedZoneId = string.Empty;
    private DashboardPeriodPreset selectedPreset;
    private DateTime trendMin;
    private DateTime trendMax;
    private double viewStart;
    private double viewEnd = 1;
    private bool busy;
    private bool hasData;

    [Inject] private UserTimeZone UserTimeZone { get; set; } = default!;

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
        granularity = Granularity;
        if (!ReferenceEquals(projectedPoints, Points) ||
            projectedGranularity != granularity || projectedZoneId != UserTimeZone.Id)
        {
            projectedPoints = Points;
            projectedGranularity = granularity;
            projectedZoneId = UserTimeZone.Id;
            points = Points.OrderBy(point => point.BucketUtc)
                .Select(point => new ChartPoint(point.BucketUtc,
                    DashboardTrendTimeLabels.ScaleDate(point.BucketUtc, granularity, UserTimeZone), point.FlightCount))
                .ToList();
            hasData = points.Any(point => point.FlightCount > 0);
            var now = DateTimeOffset.UtcNow;
            var today = DashboardLocalDateRange.Today(UserTimeZone, now);
            trendMin = points.Count == 0
                ? DashboardTrendTimeLabels.ScaleDate(UserTimeZone.DateBoundaryUtc(today, endOfDay: false), granularity, UserTimeZone)
                : points[0].CategoryDate;
            trendMax = points.Count == 0
                ? DashboardTrendTimeLabels.ScaleDate(DashboardLocalDateRange.NextMidnightUtc(UserTimeZone, now), granularity, UserTimeZone)
                : points[^1].CategoryDate;
            if (trendMax <= trendMin)
                trendMax = trendMin.AddHours(1);
        }

        rangeSummary = RangeSummary;
        rangeKey = $"{RangeKey}|{granularity}|{UserTimeZone.Id}";
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
        DashboardTrendTimeLabels.Axis(value, granularity, UserTimeZone);

    private string FormatTooltipDate(DateTimeOffset value) =>
        DashboardTrendTimeLabels.Tooltip(value, granularity, UserTimeZone);

    private DateTime ScaleToTrend(double position)
    {
        var fraction = Math.Clamp(position, 0, 1);
        return trendMin.AddTicks((long)(TrendSpan.Ticks * fraction));
    }

    private string FormatNavigatorDate(DateTime value) =>
        DashboardTrendTimeLabels.Navigator(value, granularity, UserTimeZone);

    private static string FormatCountAxis(object value) =>
        Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private sealed record PresetOption(DashboardPeriodPreset Value, string Label);

    private sealed record ChartPoint(DateTimeOffset BucketUtc, DateTime CategoryDate, long FlightCount);
}
