using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Station;

public sealed class StationId : ValueObject
{
    public Guid Value { get; }

    private StationId(Guid value) => Value = value;

    public static StationId New() => new(Guid.NewGuid());

    public static StationId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("StationId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
