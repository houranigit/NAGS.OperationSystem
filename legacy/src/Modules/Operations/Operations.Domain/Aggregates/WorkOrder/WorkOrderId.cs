using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.Aggregates.WorkOrder;

public sealed class WorkOrderId : ValueObject
{
    public Guid Value { get; }

    private WorkOrderId(Guid value) => Value = value;

    public static WorkOrderId New() => new(Guid.NewGuid());

    public static WorkOrderId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("WorkOrderId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
