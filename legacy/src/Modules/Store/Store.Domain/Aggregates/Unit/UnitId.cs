using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.Unit;

public sealed class UnitId : ValueObject
{
    public Guid Value { get; }

    private UnitId(Guid value) => Value = value;

    public static UnitId New() => new(Guid.NewGuid());

    public static UnitId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("UnitId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
