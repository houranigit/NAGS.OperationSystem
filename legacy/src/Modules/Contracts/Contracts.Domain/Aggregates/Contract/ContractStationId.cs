using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class ContractStationId : ValueObject
{
    public Guid Value { get; }

    private ContractStationId(Guid value) => Value = value;

    public static ContractStationId New() => new(Guid.NewGuid());

    public static ContractStationId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ContractStationId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
