using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

public sealed class ContractToolId : ValueObject
{
    public Guid Value { get; }

    private ContractToolId(Guid value) => Value = value;

    public static ContractToolId New() => new(Guid.NewGuid());

    public static ContractToolId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractToolId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
