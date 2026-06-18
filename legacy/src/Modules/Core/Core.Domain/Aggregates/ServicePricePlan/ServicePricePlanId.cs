using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.ServicePricePlan;

public sealed class ServicePricePlanId : ValueObject
{
    public Guid Value { get; }

    private ServicePricePlanId(Guid value) => Value = value;

    public static ServicePricePlanId New() => new(Guid.NewGuid());

    public static ServicePricePlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ServicePricePlanId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
