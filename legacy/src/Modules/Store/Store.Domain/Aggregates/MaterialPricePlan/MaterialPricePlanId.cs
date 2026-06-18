using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.MaterialPricePlan;

public sealed class MaterialPricePlanId : ValueObject
{
    public Guid Value { get; }

    private MaterialPricePlanId(Guid value) => Value = value;

    public static MaterialPricePlanId New() => new(Guid.NewGuid());

    public static MaterialPricePlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("MaterialPricePlanId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
