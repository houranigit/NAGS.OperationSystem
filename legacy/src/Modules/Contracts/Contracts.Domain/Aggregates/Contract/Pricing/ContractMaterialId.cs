using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

public sealed class ContractMaterialId : ValueObject
{
    public Guid Value { get; }

    private ContractMaterialId(Guid value) => Value = value;

    public static ContractMaterialId New() => new(Guid.NewGuid());

    public static ContractMaterialId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractMaterialId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
