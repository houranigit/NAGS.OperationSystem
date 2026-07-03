using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>A worked time window (From/To, To >= From). Duration is derived, not stored.</summary>
public sealed class TimeWindow : ValueObject
{
    private TimeWindow() { }

    private TimeWindow(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }

    public TimeSpan Duration => To - From;

    public static Result<TimeWindow> Create(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
            return Error.Validation("End time cannot be before start time.", "Operations.TimeWindow.Invalid");

        return new TimeWindow(from.ToUniversalTime(), to.ToUniversalTime());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return From;
        yield return To;
    }
}
