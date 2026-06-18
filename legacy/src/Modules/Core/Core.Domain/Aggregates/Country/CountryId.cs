using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Country;

public sealed class CountryId : ValueObject
{
    public Guid Value { get; }

    private CountryId(Guid value) => Value = value;

    public static CountryId New() => new(Guid.NewGuid());

    public static CountryId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CountryId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
