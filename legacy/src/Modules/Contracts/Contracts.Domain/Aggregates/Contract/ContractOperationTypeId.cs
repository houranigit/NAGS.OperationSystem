using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class ContractOperationTypeId : ValueObject
{
    public Guid Value { get; }

    private ContractOperationTypeId(Guid value) => Value = value;

    public static ContractOperationTypeId New() => new(Guid.NewGuid());

    public static ContractOperationTypeId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractOperationTypeId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
