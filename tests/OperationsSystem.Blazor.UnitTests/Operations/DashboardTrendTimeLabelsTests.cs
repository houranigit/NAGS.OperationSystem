using System.Globalization;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using Radzen.Blazor;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class DashboardTrendTimeLabelsTests
{
    [Fact]
    public async Task Axis_tooltip_and_navigator_show_the_same_local_date_and_time()
    {
        var zone = await DashboardLocalDateRangeTests.ChicagoAsync();
        var bucket = new DateTimeOffset(2026, 10, 5, 1, 30, 0, TimeSpan.Zero);

        DashboardTrendTimeLabels.Axis(bucket.UtcDateTime, "Hour", zone).ShouldBe("20:30");
        DashboardTrendTimeLabels.Tooltip(bucket, "Hour", zone).ShouldBe(
            zone.ToLocal(bucket).ToString("dd MMM yyyy · HH:mm zzz", CultureInfo.CurrentCulture) + " America/Chicago");
        DashboardTrendTimeLabels.Navigator(bucket.UtcDateTime, "Hour", zone).ShouldBe(
            zone.ToLocal(bucket).ToString("dd MMM · HH:mm", CultureInfo.CurrentCulture));
        zone.ToLocal(bucket).Day.ShouldBe(4);
    }

    [Fact]
    public async Task Calendar_bucket_starts_are_labeled_in_the_requested_local_month_and_day()
    {
        var zone = await DashboardLocalDateRangeTests.ChicagoAsync();
        var monthStart = new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);

        var category = DashboardTrendTimeLabels.ScaleDate(monthStart, "Month", zone);

        category.ShouldBe(new DateTime(2026, 10, 1));
        category.Kind.ShouldBe(DateTimeKind.Unspecified);
        DashboardTrendTimeLabels.Axis(category, "Month", zone).ShouldBe(new DateTime(2026, 10, 1).ToString("MMM yy"));
        DashboardTrendTimeLabels.Tooltip(monthStart, "Month", zone).ShouldBe(new DateTime(2026, 10, 1).ToString("MMMM yyyy"));
        DashboardTrendTimeLabels.Navigator(category, "Day", zone).ShouldBe(new DateTime(2026, 10, 1).ToString("dd MMM yyyy"));
    }

    [Fact]
    public async Task Repeated_fall_back_hours_keep_separate_utc_positions_and_distinct_tooltip_offsets()
    {
        var zone = await DashboardLocalDateRangeTests.ChicagoAsync();
        var first = new DashboardTimelinePoint(new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), 2);
        var second = new DashboardTimelinePoint(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), 3);

        (second.BucketDateUtc - first.BucketDateUtc).ShouldBe(TimeSpan.FromHours(1));
        DashboardTrendTimeLabels.Axis(first.BucketDateUtc, "Hour", zone).ShouldBe("01:00");
        DashboardTrendTimeLabels.Axis(second.BucketDateUtc, "Hour", zone).ShouldBe("01:00");
        DashboardTrendTimeLabels.Tooltip(first.BucketUtc, "Hour", zone).ShouldContain("-05:00 America/Chicago");
        DashboardTrendTimeLabels.Tooltip(second.BucketUtc, "Hour", zone).ShouldContain("-06:00 America/Chicago");
    }

    [Theory]
    [InlineData("Day", 10, 29, 11, 5, 600, 92, "dd MMM")]
    [InlineData("Month", 10, 1, 12, 1, 800, 300, "MMM yy")]
    public async Task Radzen_automatic_calendar_ticks_align_with_local_buckets_and_do_not_shift_date_or_month(
        string granularity, int firstMonth, int firstDay, int lastMonth, int lastDay, int width, int distance, string axisFormat)
    {
        var zone = await DashboardLocalDateRangeTests.ChicagoAsync();
        var firstUtc = zone.DateBoundaryUtc(new DateTime(2026, firstMonth, firstDay), endOfDay: false);
        var lastUtc = zone.DateBoundaryUtc(new DateTime(2026, lastMonth, lastDay), endOfDay: false);
        var firstCategory = DashboardTrendTimeLabels.ScaleDate(firstUtc, granularity, zone);
        var lastCategory = DashboardTrendTimeLabels.ScaleDate(lastUtc, granularity, zone);

        var scale = AutoDateScale(firstCategory, lastCategory, width, distance);
        var ticks = scale.TickValues(distance).Select(value => (DateTime)scale.Value(value)).ToArray();

        // The reported defect rounded November's UTC tick to midnight and labeled it October.
        ticks.ShouldContain(new DateTime(2026, 11, 1));
        foreach (var tick in ticks)
        {
            var canonicalBucket = zone.DateBoundaryUtc(tick, endOfDay: false);
            var category = DashboardTrendTimeLabels.ScaleDate(canonicalBucket, granularity, zone);
            category.ShouldBe(tick);
            scale.Scale(category.Ticks).ShouldBe(scale.Scale(tick.Ticks));
            DashboardTrendTimeLabels.Axis(scale.Value(tick.Ticks), granularity, zone).ShouldBe(tick.ToString(axisFormat));
            DashboardTrendTimeLabels.Navigator(tick, granularity, zone).ShouldBe(
                tick.ToString(granularity == "Day" ? "dd MMM yyyy" : "MMM yyyy"));
        }
    }

    [Fact]
    public async Task Radzen_hourly_auto_ticks_keep_both_fall_back_hours_at_distinct_positions()
    {
        var zone = await DashboardLocalDateRangeTests.ChicagoAsync();
        var first = new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
        var last = first.AddHours(4);
        var scale = AutoDateScale(
            DashboardTrendTimeLabels.ScaleDate(first, "Hour", zone),
            DashboardTrendTimeLabels.ScaleDate(last, "Hour", zone), 600, 92);
        var ticks = scale.TickValues(92).Select(value => (DateTime)scale.Value(value)).ToArray();
        var firstOneAm = new DateTime(2026, 11, 1, 6, 0, 0);
        var secondOneAm = firstOneAm.AddHours(1);

        ticks.ShouldContain(firstOneAm);
        ticks.ShouldContain(secondOneAm);
        scale.Scale(secondOneAm.Ticks).ShouldBeGreaterThan(scale.Scale(firstOneAm.Ticks));
        DashboardTrendTimeLabels.Axis(firstOneAm, "Hour", zone).ShouldBe("01:00");
        DashboardTrendTimeLabels.Axis(secondOneAm, "Hour", zone).ShouldBe("01:00");
        DashboardTrendTimeLabels.Tooltip(new DateTimeOffset(DateTime.SpecifyKind(firstOneAm, DateTimeKind.Utc)), "Hour", zone)
            .ShouldContain("-05:00 America/Chicago");
        DashboardTrendTimeLabels.Tooltip(new DateTimeOffset(DateTime.SpecifyKind(secondOneAm, DateTimeKind.Utc)), "Hour", zone)
            .ShouldContain("-06:00 America/Chicago");
    }

    private static ScaleBase AutoDateScale(DateTime min, DateTime max, int width, int distance)
    {
        // DateScale is internal; exercise the installed package through its public ScaleBase API.
        var type = typeof(RadzenChart).Assembly.GetType("Radzen.Blazor.DateScale", throwOnError: true)!;
        var scale = (ScaleBase)Activator.CreateInstance(type, nonPublic: true)!;
        scale.Input = new ScaleRange { Start = min.Ticks, End = max.Ticks };
        scale.Output = new ScaleRange { Start = 0, End = width };
        // Leave Step unset so Fit/TickValues follows Radzen's automatic calendar-rounding path.
        scale.Fit(distance);
        return scale;
    }
}
