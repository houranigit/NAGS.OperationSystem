using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.GeneralSupportPricePlan;

public sealed class GeneralSupportPricePlanId : ValueObject
{
    public Guid Value { get; }

    private GeneralSupportPricePlanId(Guid value) => Value = value;

    public static GeneralSupportPricePlanId New() => new(Guid.NewGuid());

    public static GeneralSupportPricePlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("GeneralSupportPricePlanId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
