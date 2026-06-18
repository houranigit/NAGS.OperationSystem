using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.WorkOrder.Commands.DeleteWorkOrder;

/// <summary>
/// Removes an under-review or rejected work order. Approved work orders must be revoked first.
/// Detaches the work order from its flight; the flight stays <c>InProgress</c>
/// (so the user can attach a different work order without re-scheduling).
/// </summary>
public sealed record DeleteWorkOrderCommand(Guid WorkOrderId) : ICommand;
