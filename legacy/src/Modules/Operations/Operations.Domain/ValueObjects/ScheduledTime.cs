using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Scheduled arrival and departure. STD must be on or after STA.</summary>
public sealed class ScheduledTime : ValueObject
{
    public DateTimeOffset Sta { get; }
    public DateTimeOffset Std { get; }

    private ScheduledTime(DateTimeOffset sta, DateTimeOffset std)
    {
        Sta = sta;
        Std = std;
    }

    public static Result<ScheduledTime> Create(DateTimeOffset sta, DateTimeOffset std)
    {
        if (std < sta)
            return Error.Validation("STD must be on or after STA.");

        return new ScheduledTime(sta, std);
    }

    public ScheduledTime WithSta(DateTimeOffset sta) => new(sta, Std);

    public ScheduledTime WithStd(DateTimeOffset std) => new(Sta, std);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Sta;
        yield return Std;
    }
}
