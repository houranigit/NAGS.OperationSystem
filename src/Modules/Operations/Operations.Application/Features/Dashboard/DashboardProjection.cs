using System.Globalization;
using Operations.Application.Contracts;

namespace Operations.Application.Features.Dashboard;

internal static class DashboardProjection
{
    public static IReadOnlyList<DashboardStatusItemDto> BuildStatuses(
        IReadOnlyList<DashboardStatusCount> counts,
        long totalFlights)
    {
        var lookup = counts.ToDictionary(item => item.Status, item => item.Count);
        return Enum.GetValues<FlightReportStatus>()
            .Select(status =>
            {
                var count = lookup.GetValueOrDefault(status);
                return new DashboardStatusItemDto(status.ToString(), count, Percentage(count, totalFlights));
            })
            .ToList();
    }

    public static DashboardFilterOptionDto[] SortOptions(
        IReadOnlyList<DashboardFilterOptionDto> options) =>
        options
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ThenBy(option => option.Id)
            .ToArray();

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildBreakdown(
        IReadOnlyList<DashboardGroupRow> groups,
        int topCount,
        long denominator)
    {
        var ordered = groups
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ThenBy(group => group.Id)
            .ToList();
        var items = ordered
            .Take(topCount)
            .Select(group => new DashboardBreakdownItemDto(
                group.Id,
                group.Label,
                group.Code,
                group.Count,
                Percentage(group.Count, denominator),
                IsOther: false,
                GroupedItemCount: 1))
            .ToList();

        var remaining = ordered.Skip(topCount).ToList();
        if (remaining.Count > 0)
        {
            var otherCount = remaining.Sum(group => group.Count);
            items.Add(new DashboardBreakdownItemDto(
                Id: null,
                Label: "Other",
                Code: null,
                otherCount,
                Percentage(otherCount, denominator),
                IsOther: true,
                GroupedItemCount: remaining.Count));
        }

        return items;
    }

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildCompleteBreakdown(
        IReadOnlyList<DashboardGroupRow> groups,
        long denominator,
        Guid? firstId = null) =>
        groups
            .OrderBy(group => firstId.HasValue && group.Id == firstId.Value ? 0 : 1)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ThenBy(group => group.Id)
            .Select(group => new DashboardBreakdownItemDto(
                group.Id,
                group.Label,
                group.Code,
                group.Count,
                Percentage(group.Count, denominator),
                IsOther: false,
                GroupedItemCount: 1))
            .ToList();

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildServiceCategories(
        long perLandingFlightCount,
        long onCallFlightCount)
    {
        var perLandingOnlyFlightCount = perLandingFlightCount - onCallFlightCount;
        return
        [
            new DashboardBreakdownItemDto(
                Id: null,
                Label: "Per Landing",
                Code: null,
                perLandingOnlyFlightCount,
                Percentage(perLandingOnlyFlightCount, perLandingFlightCount),
                IsOther: false,
                GroupedItemCount: 1),
            new DashboardBreakdownItemDto(
                Id: null,
                Label: "On Call",
                Code: null,
                onCallFlightCount,
                Percentage(onCallFlightCount, perLandingFlightCount),
                IsOther: false,
                GroupedItemCount: 1)
        ];
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildHourly(IReadOnlyList<DashboardTrendCount> counts)
    {
        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return Enumerable.Range(0, 24)
            .Select(hour => new DashboardTrendPointDto(
                hour.ToString("00", CultureInfo.InvariantCulture),
                $"{hour:00}:00",
                hour,
                lookup.GetValueOrDefault(hour)))
            .ToList();
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildMonthly(IReadOnlyList<DashboardTrendCount> counts)
    {
        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return Enumerable.Range(1, 12)
            .Select(month => new DashboardTrendPointDto(
                month.ToString("00", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month),
                month,
                lookup.GetValueOrDefault(month)))
            .ToList();
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildYearly(
        IReadOnlyList<DashboardTrendCount> counts,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        IEnumerable<int> years;
        if (fromUtc is { } from && toUtc is { } to)
        {
            var firstYear = from.Year;
            var lastYear = to.Ticks == 0 ? to.Year : to.DateTime.AddTicks(-1).Year;
            years = Enumerable.Range(firstYear, lastYear - firstYear + 1);
        }
        else
        {
            years = counts.Select(item => item.Key).Distinct().Order();
        }

        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return years.Select(year => new DashboardTrendPointDto(
            year.ToString(CultureInfo.InvariantCulture),
            year.ToString(CultureInfo.InvariantCulture),
            year,
            lookup.GetValueOrDefault(year))).ToList();
    }

    private static double Percentage(long count, long denominator) =>
        denominator <= 0
            ? 0
            : Math.Round(count * 100d / denominator, 2, MidpointRounding.AwayFromZero);
}

internal enum DashboardTimelineGranularity
{
    Hour,
    Day,
    Month
}

internal static class DashboardTimelineProjection
{
    public static DashboardTimelineGranularity SelectGranularity(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeZoneInfo? timeZone = null)
    {
        if (fromUtc is null || toUtc is null)
            return DashboardTimelineGranularity.Month;

        var duration = timeZone is null
            ? toUtc.Value - fromUtc.Value
            : TimeZoneInfo.ConvertTime(toUtc.Value, timeZone).DateTime
                - TimeZoneInfo.ConvertTime(fromUtc.Value, timeZone).DateTime;
        if (duration <= TimeSpan.FromDays(2))
            return DashboardTimelineGranularity.Hour;
        if (duration <= TimeSpan.FromDays(120))
            return DashboardTimelineGranularity.Day;

        return DashboardTimelineGranularity.Month;
    }

    public static DashboardTimelineGranularity SelectMaxGranularity(
        DateTimeOffset firstFlightUtc,
        DateTimeOffset lastFlightUtc,
        TimeZoneInfo? timeZone = null)
    {
        var first = TimeZoneInfo.ConvertTime(firstFlightUtc, timeZone ?? TimeZoneInfo.Utc);
        var last = TimeZoneInfo.ConvertTime(lastFlightUtc, timeZone ?? TimeZoneInfo.Utc);
        if (first.Year == last.Year && first.DayOfYear == last.DayOfYear)
            return DashboardTimelineGranularity.Hour;
        if (first.Year == last.Year && first.Month == last.Month)
            return DashboardTimelineGranularity.Day;

        return DashboardTimelineGranularity.Month;
    }

    public static IReadOnlyList<DashboardTimelinePointDto> BuildLocal(
        IReadOnlyList<DateTimeOffset> arrivalInstants,
        DashboardTimelineGranularity granularity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeZoneInfo timeZone)
    {
        var lookup = arrivalInstants.GroupBy(instant => FloorToLocalBucket(instant, granularity, timeZone))
            .ToDictionary(group => group.Key, group => group.LongCount());
        if (lookup.Count == 0 && (fromUtc is null || toUtc is null))
            return [];

        var firstBucket = fromUtc is { } from
            ? FloorToLocalBucket(from, granularity, timeZone)
            : lookup.Keys.Min();
        var endExclusive = toUtc ?? AddLocalBucket(lookup.Keys.Max(), granularity, timeZone);
        var points = new List<DashboardTimelinePointDto>();
        for (var bucket = firstBucket; bucket < endExclusive; bucket = AddLocalBucket(bucket, granularity, timeZone))
            points.Add(new DashboardTimelinePointDto(bucket, lookup.GetValueOrDefault(bucket)));
        return points;
    }

    private static DateTimeOffset FloorToLocalBucket(
        DateTimeOffset instant,
        DashboardTimelineGranularity granularity,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        if (granularity == DashboardTimelineGranularity.Hour)
        {
            // Retain the instant's offset: repeated fall-back hours are distinct UTC buckets.
            var floor = ToUtc(new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0), local.Offset);
            // A partial-hour DST transition may make that nominal hour start invalid for this
            // offset. Its bucket starts at the actual transition instead (e.g. Lord Howe).
            return timeZone.GetUtcOffset(floor) == local.Offset
                ? floor
                : FirstOffsetChange(floor, instant.ToUniversalTime(), timeZone) ?? floor;
        }

        var date = granularity == DashboardTimelineGranularity.Day
            ? local.Date
            : new DateTime(local.Year, local.Month, 1);
        return ResolveLocalPeriodStart(date, timeZone);
    }

    private static DateTimeOffset AddLocalBucket(
        DateTimeOffset bucketUtc,
        DashboardTimelineGranularity granularity,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(bucketUtc, timeZone).DateTime;
        if (local.Year == DateTime.MaxValue.Year && local.Month == 12 &&
            (granularity == DashboardTimelineGranularity.Month || local.Day == 31 &&
                (granularity == DashboardTimelineGranularity.Day || local.Hour == 23)))
            return DateTimeOffset.MaxValue;

        if (granularity == DashboardTimelineGranularity.Hour)
        {
            var nextLocalHour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0)
                .AddHours(1);
            var next = ResolveLocalPeriodStart(nextLocalHour, timeZone, bucketUtc);
            while (next <= bucketUtc)
            {
                nextLocalHour = nextLocalHour.AddHours(1);
                next = ResolveLocalPeriodStart(nextLocalHour, timeZone, bucketUtc);
            }
            // Offset changes split a repeated hour or partial-hour transition into real, distinct
            // intervals; nonexistent spring hours are skipped by resolving the next valid boundary.
            return FirstOffsetChange(bucketUtc, next, timeZone) ?? next;
        }
        return ResolveLocalPeriodStart(
            granularity == DashboardTimelineGranularity.Day ? local.Date.AddDays(1) : local.Date.AddMonths(1),
            timeZone);
    }

    private static DateTimeOffset? FirstOffsetChange(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(fromUtc);
        for (var cursor = fromUtc; cursor < toUtc;)
        {
            var probe = toUtc - cursor <= TimeSpan.FromMinutes(15) ? toUtc : cursor.AddMinutes(15);
            if (timeZone.GetUtcOffset(probe) != offset)
            {
                var low = cursor.UtcTicks;
                var high = probe.UtcTicks;
                while (high - low > 1)
                {
                    var middle = low + (high - low) / 2;
                    if (timeZone.GetUtcOffset(new DateTimeOffset(middle, TimeSpan.Zero)) == offset)
                        low = middle;
                    else
                        high = middle;
                }
                return new DateTimeOffset(high, TimeSpan.Zero);
            }
            cursor = probe;
        }
        return null;
    }

    private static DateTimeOffset ResolveLocalPeriodStart(
        DateTime local,
        TimeZoneInfo timeZone,
        DateTimeOffset? afterUtc = null)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Some zones move their clocks at midnight. Start that calendar bucket at its first valid
        // instant, using the earlier occurrence when midnight is repeated.
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offsets = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local)
            : [timeZone.GetUtcOffset(local)];
        var candidates = offsets.Select(offset => ToUtc(local, offset))
            .Order().ToArray();
        // When advancing hourly buckets, a repeated boundary's first occurrence may already have
        // passed. Choose its next occurrence before advancing to another wall-clock hour.
        return candidates.FirstOrDefault(candidate => afterUtc is null || candidate > afterUtc.Value, candidates[0]);
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeSpan offset) =>
        new(Math.Clamp(local.Ticks - offset.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), TimeSpan.Zero);
}

internal sealed record DashboardStatusCount(FlightReportStatus Status, long Count);
internal sealed record DashboardGroupRow(Guid Id, string Label, string? Code, long Count);
internal sealed record DashboardTrendCount(int Key, long Count);
