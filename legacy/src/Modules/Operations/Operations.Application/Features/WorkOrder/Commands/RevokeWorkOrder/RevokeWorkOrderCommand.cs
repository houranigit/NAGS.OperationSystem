using BuildingBlocks.Application.Abstractions.Commands;

namespace Operations.Application.Features.WorkOrder.Commands.RevokeWorkOrder;

/// <summary>
/// Reverses an approval: the work order returns to <c>UnderReview</c>, the flight clears
/// its <c>AcceptedWorkOrder</c> and reopens to <c>InProgress</c> so a different work order
/// can be approved instead.
/// </summary>
public sealed record RevokeWorkOrderCommand(Guid WorkOrderId) : ICommand;
