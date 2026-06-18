using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Currency;

public sealed class ExchangeRateId : ValueObject
{
    public Guid Value { get; }

    private ExchangeRateId(Guid value) => Value = value;

    public static ExchangeRateId New() => new(Guid.NewGuid());

    public static ExchangeRateId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ExchangeRateId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
