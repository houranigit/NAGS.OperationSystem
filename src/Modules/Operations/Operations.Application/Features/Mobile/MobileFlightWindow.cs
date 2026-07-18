namespace Operations.Application.Features.Mobile;

/// <summary>
/// Defines the mobile flight visibility/action window around scheduled arrival (STA).
/// The default window is inclusive: a flight is available from twelve hours before STA
/// through twelve hours after STA.
/// </summary>
public static class MobileFlightWindow
{
    public const int DefaultHours = 12;
    public const int MinHours = 1;
    public const int MaxHours = 168;

    public static int ClampHours(int hours) => Math.Clamp(hours, MinHours, MaxHours);

    public static MobileFlightWindowState Evaluate(
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset nowUtc,
        int windowHours = DefaultHours)
    {
        var window = TimeSpan.FromHours(ClampHours(windowHours));
        var startsAtUtc = scheduledArrivalUtc - window;
        var endsAtUtc = scheduledArrivalUtc + window;

        return new MobileFlightWindowState(
            nowUtc >= startsAtUtc && nowUtc <= endsAtUtc,
            startsAtUtc,
            endsAtUtc);
    }
}

public readonly record struct MobileFlightWindowState(
    bool IsWithinWindow,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);
