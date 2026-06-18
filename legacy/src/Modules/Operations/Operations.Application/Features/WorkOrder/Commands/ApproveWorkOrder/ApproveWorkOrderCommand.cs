using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.WorkOrder.Commands.ApproveWorkOrder;

/// <summary>
/// Approves an under-review work order: it becomes the flight's <c>AcceptedWorkOrder</c>,
/// the flight is settled (Completed or Canceled), and every other under-review work order
/// on the same flight is moved to <c>Deleting</c> — the deletion job will hard-remove
/// them once the configured grace period elapses, or they are restored to
/// <c>UnderReview</c> if this approval is revoked first.
/// </summary>
public sealed record ApproveWorkOrderCommand(Guid WorkOrderId) : ICommand;
