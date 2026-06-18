using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class ContractAdvancePaymentId : ValueObject
{
    public Guid Value { get; }

    private ContractAdvancePaymentId(Guid value) => Value = value;

    public static ContractAdvancePaymentId New() => new(Guid.NewGuid());

    public static ContractAdvancePaymentId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractAdvancePaymentId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
