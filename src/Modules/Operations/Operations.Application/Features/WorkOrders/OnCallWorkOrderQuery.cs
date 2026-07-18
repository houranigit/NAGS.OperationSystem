using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

/// <summary>
/// The persisted work orders that make a Per Landing flight On Call. Keeping this as an
/// <see cref="IQueryable{T}"/> composition lets every caller reuse the same EF-translatable rule.
/// </summary>
internal static class OnCallWorkOrderQuery
{
    public static IQueryable<WorkOrder> QualifyingForOnCall(this IQueryable<WorkOrder> query) =>
        query.Where(workOrder =>
            workOrder.Status != WorkOrderStatus.Merged &&
            workOrder.ServiceLines.Any());
}
