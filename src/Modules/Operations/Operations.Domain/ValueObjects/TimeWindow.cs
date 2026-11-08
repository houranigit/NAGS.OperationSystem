using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>An actual work period with a required start and an optional end.</summary>
public sealed class TimeWindow : ValueObject
{
    private TimeWindow() { }

    public DateTimeOffset FromUtc { get; private set; }
    public DateTimeOffset? ToUtc { get; private set; }

    public static Result<TimeWindow> Create(DateTimeOffset from, DateTimeOffset? to)
    {
        if (from == default)
            return Error.Validation("Work start time is required.", "Operations.TimeWindow.StartRequired");
        if (to.HasValue && to.Value < from)
            return Error.Validation("Work end time cannot be before its start.", "Operations.TimeWindow.EndBeforeStart");

        return new TimeWindow { FromUtc = from.ToUniversalTime(), ToUtc = to?.ToUniversalTime() };
    }

    internal TimeWindow Copy() => Create(FromUtc, ToUtc).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FromUtc;
        yield return ToUtc;
    }
}
