
namespace Operations.Contracts.WorkOrder;

public sealed record WorkOrderSnapshot(
    Guid WorkOrderId,
    string? WorkOrderNo);
