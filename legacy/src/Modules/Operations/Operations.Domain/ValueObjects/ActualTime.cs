using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Actual arrival and departure. ATD must be on or after ATA when both are used.</summary>
public sealed class ActualTime : ValueObject
{
    public DateTimeOffset Ata { get; }
    public DateTimeOffset Atd { get; }

    private ActualTime(DateTimeOffset ata, DateTimeOffset atd)
    {
        Ata = ata;
        Atd = atd;
    }

    public static Result<ActualTime> Create(DateTimeOffset ata, DateTimeOffset atd)
    {
        if (atd < ata)
            return Error.Validation("ATD must be on or after ATA.");

        return new ActualTime(ata, atd);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Ata;
        yield return Atd;
    }
}
