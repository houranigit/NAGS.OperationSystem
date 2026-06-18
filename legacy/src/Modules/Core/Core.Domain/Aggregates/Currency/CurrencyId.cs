using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Currency;

public sealed class CurrencyId : ValueObject
{
    public Guid Value { get; }

    private CurrencyId(Guid value) => Value = value;

    public static CurrencyId New() => new(Guid.NewGuid());

    public static CurrencyId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CurrencyId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
