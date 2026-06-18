using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

public sealed class ContractManpowerId : ValueObject
{
    public Guid Value { get; }

    private ContractManpowerId(Guid value) => Value = value;

    public static ContractManpowerId New() => new(Guid.NewGuid());

    public static ContractManpowerId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractManpowerId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
