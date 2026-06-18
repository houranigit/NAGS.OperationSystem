using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.ManpowerPricePlan;

public sealed class ManpowerPricePlanId : ValueObject
{
    public Guid Value { get; }

    private ManpowerPricePlanId(Guid value) => Value = value;

    public static ManpowerPricePlanId New() => new(Guid.NewGuid());

    public static ManpowerPricePlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ManpowerPricePlanId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
