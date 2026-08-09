using System.Globalization;

namespace Operations.Api.Exports;

/// <summary>
/// Resolves the browser time-zone identifiers accepted by Operations file-generation boundaries.
/// Stored/query instants remain UTC; the resolved zone is used only while presenting a file.
/// </summary>
internal static class FlightExportTimeZoneResolver
{
    private const string BrowserPrefix = "Browser UTC";
    internal const string DefaultTimeZoneId = "Asia/Riyadh";
    private const int MaximumIdLength = 128;

    public static TimeZoneInfo ResolveDefault() => ResolveRiyadhFallback();

    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = ResolveRiyadhFallback();
            return true;
        }

        var candidate = timeZoneId.Trim();
        if (candidate.Length > MaximumIdLength)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        if (candidate.StartsWith(BrowserPrefix, StringComparison.OrdinalIgnoreCase))
            return TryResolveBrowserOffset(candidate, out timeZone);

        if (TryFind(candidate, out timeZone))
            return true;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(candidate, out var windowsId) &&
            TryFind(windowsId, out timeZone))
        {
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId) &&
            TryFind(ianaId, out timeZone))
        {
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static bool TryResolveBrowserOffset(string candidate, out TimeZoneInfo timeZone)
    {
        var suffix = candidate.AsSpan(BrowserPrefix.Length);
        if (suffix.Length != 6 || suffix[0] is not ('+' or '-'))
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        if (!TimeSpan.TryParseExact(
                suffix[1..],
                "hh\\:mm",
                CultureInfo.InvariantCulture,
                out var absoluteOffset) ||
            absoluteOffset > TimeSpan.FromHours(14) ||
            (absoluteOffset.Hours == 14 && absoluteOffset.Minutes != 0))
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        var offset = suffix[0] == '-' ? -absoluteOffset : absoluteOffset;
        try
        {
            timeZone = TimeZoneInfo.CreateCustomTimeZone(candidate, offset, candidate, candidate);
            return true;
        }
        catch (ArgumentException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static TimeZoneInfo ResolveRiyadhFallback()
    {
        if (TryFind(DefaultTimeZoneId, out var riyadh))
            return riyadh;

        return TimeZoneInfo.CreateCustomTimeZone(
            DefaultTimeZoneId,
            TimeSpan.FromHours(3),
            DefaultTimeZoneId,
            DefaultTimeZoneId);
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
