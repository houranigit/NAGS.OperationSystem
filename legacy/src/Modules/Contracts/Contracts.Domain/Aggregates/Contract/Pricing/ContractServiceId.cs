using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

public sealed class ContractServiceId : ValueObject
{
    public Guid Value { get; }

    private ContractServiceId(Guid value) => Value = value;

    public static ContractServiceId New() => new(Guid.NewGuid());

    public static ContractServiceId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractServiceId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
