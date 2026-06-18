using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.WorkOrder.Commands.RejectWorkOrder;

/// <summary>
/// Manually rejects an under-review work order. Does not affect the flight's status —
/// the flight stays <c>InProgress</c> until another work order is approved or all are removed.
/// </summary>
public sealed record RejectWorkOrderCommand(Guid WorkOrderId) : ICommand;
