using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.OperationType;

public sealed class OperationTypeId : ValueObject
{
    public Guid Value { get; }

    private OperationTypeId(Guid value) => Value = value;

    public static OperationTypeId New() => new(Guid.NewGuid());

    public static OperationTypeId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OperationTypeId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
