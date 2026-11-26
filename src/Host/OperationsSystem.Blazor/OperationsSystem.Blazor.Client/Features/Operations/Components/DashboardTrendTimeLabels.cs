using System.Globalization;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

/// <summary>Maps calendar chart positions and formats labels while preserving canonical UTC bucket instants.</summary>
internal static class DashboardTrendTimeLabels
{
    // Calendar scales must use wall-clock coordinates so Radzen's midnight/month tick rounding
    // matches the plotted calendar buckets. Hourly scales stay UTC to preserve repeated DST hours.
    public static DateTime ScaleDate(DateTimeOffset bucketUtc, string granularity, UserTimeZone timeZone) =>
        granularity == "Hour" ? bucketUtc.UtcDateTime : timeZone.ToLocalDateTime(bucketUtc).Date;

    public static string Axis(object value, string granularity, UserTimeZone timeZone)
    {
        var position = value switch
        {
            DateTimeOffset offset => granularity == "Hour" ? offset.UtcDateTime : offset.DateTime,
            DateTime dateTime => dateTime,
            _ => (DateTime?)null
        };
        return position is { } date
            ? FormatScaleDate(date, granularity, timeZone, granularity switch
            {
                "Hour" => "HH:mm",
                "Day" => "dd MMM",
                _ => "MMM yy"
            })
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public static string Tooltip(DateTimeOffset bucketUtc, string granularity, UserTimeZone timeZone) =>
        granularity switch
        {
            // The numeric offset distinguishes repeated wall-clock hours when daylight saving ends.
            "Hour" => $"{timeZone.Format(bucketUtc, "dd MMM yyyy · HH:mm zzz")} {timeZone.Id}",
            "Day" => timeZone.Format(bucketUtc, "dddd, dd MMM yyyy"),
            _ => timeZone.Format(bucketUtc, "MMMM yyyy")
        };

    public static string Navigator(DateTime position, string granularity, UserTimeZone timeZone) =>
        FormatScaleDate(position, granularity, timeZone, granularity switch
        {
            "Hour" => "dd MMM · HH:mm",
            "Day" => "dd MMM yyyy",
            _ => "MMM yyyy"
        });

    private static string FormatScaleDate(DateTime position, string granularity, UserTimeZone timeZone, string format) =>
        granularity == "Hour"
            ? timeZone.Format(AsUtc(position), format)
            : position.ToString(format, CultureInfo.CurrentCulture);

    private static DateTimeOffset AsUtc(DateTime date) =>
        new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
}
