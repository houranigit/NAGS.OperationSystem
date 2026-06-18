using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.Aggregates.Flight;

public sealed class FlightId : ValueObject
{
    public Guid Value { get; }

    private FlightId(Guid value) => Value = value;

    public static FlightId New() => new(Guid.NewGuid());

    public static FlightId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("FlightId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
