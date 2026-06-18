using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class ContractId : ValueObject
{
    public Guid Value { get; }

    private ContractId(Guid value) => Value = value;

    public static ContractId New() => new(Guid.NewGuid());

    public static ContractId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
