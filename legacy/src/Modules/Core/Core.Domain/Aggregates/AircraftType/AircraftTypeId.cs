using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.AircraftType;

public sealed class AircraftTypeId : ValueObject
{
    public Guid Value { get; }

    private AircraftTypeId(Guid value) => Value = value;

    public static AircraftTypeId New() => new(Guid.NewGuid());

    public static AircraftTypeId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("AircraftTypeId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
