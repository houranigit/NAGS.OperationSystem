using BuildingBlocks.Domain.ValueObjects;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.ValueObjects;

/// <summary>Canonical approved work order linked to a flight (settled state).</summary>
public sealed class WorkOrderSnapshot : ValueObject
{
    public WorkOrderId WorkOrderId { get; }
    public WorkOrderNumber WorkOrderNumber { get; }

    private WorkOrderSnapshot(WorkOrderId workOrderId, WorkOrderNumber workOrderNumber)
    {
        WorkOrderId = workOrderId;
        WorkOrderNumber = workOrderNumber;
    }

    public static WorkOrderSnapshot Create(WorkOrderId workOrderId, WorkOrderNumber workOrderNumber) =>
        new(workOrderId, workOrderNumber);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return WorkOrderId;
        yield return WorkOrderNumber;
    }
}
