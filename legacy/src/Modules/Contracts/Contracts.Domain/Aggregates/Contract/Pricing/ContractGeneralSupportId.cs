using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

public sealed class ContractGeneralSupportId : ValueObject
{
    public Guid Value { get; }

    private ContractGeneralSupportId(Guid value) => Value = value;

    public static ContractGeneralSupportId New() => new(Guid.NewGuid());

    public static ContractGeneralSupportId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractGeneralSupportId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
