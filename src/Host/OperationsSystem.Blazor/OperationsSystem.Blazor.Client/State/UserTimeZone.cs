using Microsoft.JSInterop;

namespace OperationsSystem.Blazor.Client.State;

/// <summary>
/// Holds the browser's IANA time zone for the current portal session. API contracts and storage
/// remain UTC; this service is the single presentation-boundary conversion point.
/// </summary>
public sealed class UserTimeZone(IJSRuntime js)
{
    private Task? initialization;
    private TimeZoneInfo timeZone = TimeZoneInfo.Utc;

    public string Id => timeZone.Id;
    public string DisplayName => timeZone.DisplayName;
    public bool IsUtc => timeZone.Equals(TimeZoneInfo.Utc);

    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    public DateTimeOffset ToLocal(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, timeZone);

    public DateTime ToLocalDateTime(DateTimeOffset value) =>
        DateTime.SpecifyKind(ToLocal(value).DateTime, DateTimeKind.Unspecified);

    public DateTime? ToLocalDateTime(DateTimeOffset? value) =>
        value is null ? null : ToLocalDateTime(value.Value);

    public bool TryToUtc(DateTime? localValue, out DateTimeOffset? utcValue, out string? error)
        => TryToUtc(localValue, timeZone, out utcValue, out error);

    internal static bool TryToUtc(
        DateTime? localValue,
        TimeZoneInfo timeZone,
        out DateTimeOffset? utcValue,
        out string? error)
    {
        if (localValue is null)
        {
            utcValue = null;
            error = null;
            return true;
        }

        var local = DateTime.SpecifyKind(localValue.Value, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            utcValue = null;
            error = "This local time does not exist because the clock moves forward. Choose another time.";
            return false;
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            utcValue = null;
            error = "This local time occurs twice because the clock moves back. Choose a time outside the repeated hour.";
            return false;
        }

        utcValue = new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
        error = null;
        return true;
    }

    public DateTimeOffset? ToUtc(DateTime? localValue)
    {
        if (!TryToUtc(localValue, out var utcValue, out var error))
            throw new InvalidOperationException(error);

        return utcValue;
    }

    public string Format(DateTimeOffset value, string format = "yyyy-MM-dd HH:mm") =>
        ToLocal(value).ToString(format);

    /// <summary>
    /// Converts a date-only filter boundary in the selected zone to the UTC instant expected by
    /// the API. End boundaries are inclusive. A midnight skipped by a zone-rule transition is
    /// advanced to that day's first valid instant; an overlap selects the earlier instant.
    /// </summary>
    public DateTimeOffset DateBoundaryUtc(DateTime date, bool endOfDay)
        => DateBoundaryUtc(date, endOfDay, timeZone);

    internal static DateTimeOffset DateBoundaryUtc(DateTime date, bool endOfDay, TimeZoneInfo timeZone)
    {
        var boundary = DateTime.SpecifyKind(date.Date.AddDays(endOfDay ? 1 : 0), DateTimeKind.Unspecified);
        var searchLimit = boundary.AddDays(2);
        while (timeZone.IsInvalidTime(boundary) && boundary < searchLimit)
            boundary = boundary.AddMinutes(1);

        if (timeZone.IsInvalidTime(boundary))
            throw new InvalidOperationException("The selected date does not have a valid boundary in your time zone.");

        var offset = timeZone.IsAmbiguousTime(boundary)
            ? timeZone.GetAmbiguousTimeOffsets(boundary).Max()
            : timeZone.GetUtcOffset(boundary);
        var instant = new DateTimeOffset(boundary, offset).ToUniversalTime();
        return endOfDay ? instant.AddTicks(-1) : instant;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var browser = await js.InvokeAsync<BrowserTimeZone>("operationsSystem.timeZone.get");
            timeZone = Resolve(browser.Id, browser.OffsetMinutes);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                timeZone = TimeZoneInfo.Utc;
            }
        }
    }

    internal static TimeZoneInfo Resolve(string? id, int offsetMinutes)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
                {
                    try
                    {
                        return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                    }
                    catch (TimeZoneNotFoundException)
                    {
                        // Fall through to the browser's current offset.
                    }
                    catch (InvalidTimeZoneException)
                    {
                        // Fall through to the browser's current offset.
                    }
                }
            }
            catch (InvalidTimeZoneException)
            {
                // Fall through to the browser's current offset.
            }
        }

        var boundedMinutes = Math.Clamp(offsetMinutes, -14 * 60, 14 * 60);
        var offset = TimeSpan.FromMinutes(boundedMinutes);
        if (offset == TimeSpan.Zero)
            return TimeZoneInfo.Utc;

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var fallbackId = $"Browser UTC{sign}{absolute:hh\\:mm}";
        return TimeZoneInfo.CreateCustomTimeZone(fallbackId, offset, fallbackId, fallbackId);
    }

    private sealed record BrowserTimeZone(string? Id, int OffsetMinutes);
}
