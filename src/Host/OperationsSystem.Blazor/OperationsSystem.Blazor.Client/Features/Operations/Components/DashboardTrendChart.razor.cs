using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class DashboardTrendChart
{
    private const double StandardChartWidth = 720d;
    private const double DenseChartWidth = 1240d;
    private const double ChartHeight = 190d;
    private const double PlotTop = 8d;
    private const double PlotBottom = 151d;
    private const double PlotLeft = 44d;
    private const double PlotEndPadding = 12d;
    private const double MaximumBarWidth = 30d;
    private const double MinimumVisibleBarHeight = 2d;
    private const double YLabelX = PlotLeft - 8d;
    private const double XLabelY = 178d;

    private readonly string titleId = $"dtc-title-{Guid.NewGuid():N}";
    private readonly string descriptionId = $"dtc-description-{Guid.NewGuid():N}";

    private string title = string.Empty;
    private string description = string.Empty;
    private string icon = string.Empty;
    private string emptyText = string.Empty;
    private bool hasData;
    private double chartWidth = StandardChartWidth;
    private double plotRight = StandardChartWidth - PlotEndPadding;
    private IReadOnlyList<DashboardTrendPoint> points = [];
    private IReadOnlyList<GridLine> gridLines = [];
    private IReadOnlyList<BarLayout> bars = [];

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Description { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Icon { get; set; } = string.Empty;
    [Parameter, EditorRequired] public IReadOnlyList<DashboardTrendPoint> Points { get; set; } = [];
    [Parameter, EditorRequired] public string EmptyText { get; set; } = string.Empty;
    [Parameter] public string FlightsLabel { get; set; } = "flights";

    private static double PlotHeight => PlotBottom - PlotTop;
    private double PlotWidth => plotRight - PlotLeft;

    private string SvgClass =>
        points.Count >= 18
            ? "dtc-chart dtc-chart--dense"
            : "dtc-chart";

    private string ViewBox =>
        $"0 0 {SvgNumber(chartWidth)} {SvgNumber(ChartHeight)}";

    private string ChartTooltip =>
        string.IsNullOrWhiteSpace(description)
            ? title
            : $"{title}. {description}";

    protected override void OnParametersSet()
    {
        title = Title;
        description = Description;
        icon = Icon;
        emptyText = EmptyText;
        points = Points
            .OrderBy(point => point.SortOrder)
            .ToArray();
        chartWidth = points.Count >= 18
            ? DenseChartWidth
            : StandardChartWidth;
        plotRight = chartWidth - PlotEndPadding;
        hasData = points.Any(point => point.FlightCount > 0);

        if (!hasData)
        {
            gridLines = [];
            bars = [];
            return;
        }

        var scale = CalculateScale(points);
        gridLines = BuildGridLines(scale);
        bars = BuildBars(points, scale.Maximum, LabelStep(points.Count), PlotWidth);
    }

    private static Scale CalculateScale(IReadOnlyList<DashboardTrendPoint> source)
    {
        var maximumValue = source.Max(point => Math.Max(0L, point.FlightCount));
        var scaleTarget = Math.Max(4d, maximumValue);
        var rawStep = scaleTarget / 4d;
        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(rawStep)));
        var normalizedStep = rawStep / magnitude;

        var niceFactor = normalizedStep switch
        {
            <= 1d => 1d,
            <= 2d => 2d,
            <= 2.5d => 2.5d,
            <= 5d => 5d,
            _ => 10d
        };

        var step = Math.Max(1d, niceFactor * magnitude);
        var maximum = Math.Ceiling(scaleTarget / step) * step;
        var intervalCount = Math.Max(1, (int)Math.Round(maximum / step));

        return new Scale(maximum, step, intervalCount);
    }

    private static IReadOnlyList<GridLine> BuildGridLines(Scale scale)
    {
        var result = new List<GridLine>(scale.IntervalCount + 1);

        for (var index = 0; index <= scale.IntervalCount; index++)
        {
            var value = scale.Maximum - (index * scale.Step);
            var y = PlotTop + ((double)index / scale.IntervalCount * PlotHeight);
            var isBaseline = index == scale.IntervalCount;

            result.Add(new GridLine(
                $"grid-{index}",
                y,
                Math.Max(0d, value).ToString("N0", CultureInfo.CurrentCulture),
                isBaseline ? "dtc-grid-line dtc-grid-line--baseline" : "dtc-grid-line"));
        }

        return result;
    }

    private static IReadOnlyList<BarLayout> BuildBars(
        IReadOnlyList<DashboardTrendPoint> source,
        double scaleMaximum,
        int labelStep,
        double plotWidth)
    {
        var result = new List<BarLayout>(source.Count);
        var slotWidth = plotWidth / source.Count;
        var barWidth = Math.Min(MaximumBarWidth, Math.Max(5d, slotWidth * 0.54d));

        for (var index = 0; index < source.Count; index++)
        {
            var point = source[index];
            var value = Math.Max(0L, point.FlightCount);
            var calculatedHeight = value / scaleMaximum * PlotHeight;
            var height = value == 0
                ? 0d
                : Math.Max(MinimumVisibleBarHeight, calculatedHeight);
            var x = PlotLeft + (index * slotWidth) + ((slotWidth - barWidth) / 2d);
            var y = PlotBottom - height;

            result.Add(new BarLayout(
                $"{point.Key}:{index}",
                point,
                x,
                y,
                barWidth,
                height,
                PlotLeft + ((index + 0.5d) * slotWidth),
                index % labelStep == 0));
        }

        return result;
    }

    private static int LabelStep(int pointCount) =>
        pointCount switch
        {
            <= 12 => 1,
            >= 24 => 3,
            _ => 2
        };

    private string BarTooltip(DashboardTrendPoint point) =>
        $"{point.Label}: {Math.Max(0L, point.FlightCount).ToString("N0", CultureInfo.CurrentCulture)} {FlightsLabel}";

    private static string SvgNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record Scale(double Maximum, double Step, int IntervalCount);

    private sealed record GridLine(string Key, double Y, string Label, string CssClass);

    private sealed record BarLayout(
        string Key,
        DashboardTrendPoint Point,
        double X,
        double Y,
        double Width,
        double Height,
        double LabelX,
        bool ShowLabel);
}
