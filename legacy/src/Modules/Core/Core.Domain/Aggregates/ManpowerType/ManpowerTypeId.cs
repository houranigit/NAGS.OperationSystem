using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.ManpowerType;

public sealed class ManpowerTypeId : ValueObject
{
    public Guid Value { get; }

    private ManpowerTypeId(Guid value) => Value = value;

    public static ManpowerTypeId New() => new(Guid.NewGuid());

    public static ManpowerTypeId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ManpowerTypeId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
