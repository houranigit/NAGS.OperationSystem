using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Actual times of a flight: ATA (arrival) and ATD (departure), with ATD >= ATA.</summary>
public sealed class ActualTime : ValueObject
{
    private ActualTime() { }

    private ActualTime(DateTimeOffset ata, DateTimeOffset atd)
    {
        Ata = ata;
        Atd = atd;
    }

    /// <summary>Actual Time of Arrival (UTC).</summary>
    public DateTimeOffset Ata { get; private set; }

    /// <summary>Actual Time of Departure (UTC).</summary>
    public DateTimeOffset Atd { get; private set; }

    public static Result<ActualTime> Create(DateTimeOffset ata, DateTimeOffset atd)
    {
        if (atd < ata)
            return Error.Validation("Actual departure cannot be before actual arrival.", "Operations.ActualTime.Invalid");

        return new ActualTime(ata.ToUniversalTime(), atd.ToUniversalTime());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Ata;
        yield return Atd;
    }
}
