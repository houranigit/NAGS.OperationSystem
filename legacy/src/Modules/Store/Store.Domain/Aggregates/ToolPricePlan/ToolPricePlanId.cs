using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.ToolPricePlan;

public sealed class ToolPricePlanId : ValueObject
{
    public Guid Value { get; }

    private ToolPricePlanId(Guid value) => Value = value;

    public static ToolPricePlanId New() => new(Guid.NewGuid());

    public static ToolPricePlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ToolPricePlanId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
