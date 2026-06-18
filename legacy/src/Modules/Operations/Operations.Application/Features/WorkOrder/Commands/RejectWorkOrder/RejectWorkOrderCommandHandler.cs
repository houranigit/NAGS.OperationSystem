using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Application.Features.WorkOrder.Commands.RejectWorkOrder;

public sealed class RejectWorkOrderCommandHandler(IWorkOrderRepository workOrders)
    : ICommandHandler<RejectWorkOrderCommand>
{
    public async Task<Result> Handle(RejectWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkOrderId == Guid.Empty)
            return Error.Validation("Work order id is required.");

        var workOrderId = WorkOrderId.From(request.WorkOrderId);
        var workOrder = await workOrders.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.");

        var rejected = workOrder.Reject();
        if (rejected.IsFailure)
            return rejected;

        workOrders.Update(workOrder);
        return Result.Success();
    }
}
