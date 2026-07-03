using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Scheduled times of a flight: STA (arrival) and STD (departure), with STD >= STA.</summary>
public sealed class ScheduledTime : ValueObject
{
    private ScheduledTime() { }

    private ScheduledTime(DateTimeOffset sta, DateTimeOffset std)
    {
        Sta = sta;
        Std = std;
    }

    /// <summary>Scheduled Time of Arrival (UTC).</summary>
    public DateTimeOffset Sta { get; private set; }

    /// <summary>Scheduled Time of Departure (UTC).</summary>
    public DateTimeOffset Std { get; private set; }

    public static Result<ScheduledTime> Create(DateTimeOffset sta, DateTimeOffset std)
    {
        if (std < sta)
            return Error.Validation("Scheduled departure cannot be before scheduled arrival.", "Operations.ScheduledTime.Invalid");

        return new ScheduledTime(sta.ToUniversalTime(), std.ToUniversalTime());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Sta;
        yield return Std;
    }
}
