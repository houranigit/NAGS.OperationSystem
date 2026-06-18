using Microsoft.EntityFrameworkCore;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;
using OpsFlight = Operations.Domain.Aggregates.Flight;
using WorkOrderEntity = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Infrastructure.Persistence.Repositories;

public sealed class WorkOrderRepository(OperationsDbContext context) : IWorkOrderRepository
{
    public async Task<WorkOrderEntity?> GetByIdAsync(
        WorkOrderId id,
        CancellationToken cancellationToken = default) =>
        await context.WorkOrders
            .Include("_serviceLines")
            .Include("_tasks._employees")
            .Include("_tasks._tools")
            .Include("_tasks._materials")
            .Include("_tasks._generalSupports")
            .Include("_tasks._attachments")
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkOrderEntity>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Array.Empty<WorkOrderEntity>();

        // Compare using the same CLR type as the mapped property. Mixing `Guid` (e.g. via
        // `EF.Property<Guid>(...)`) with `Contains` against a value-converted `WorkOrderId` key
        // makes EF10 compose incompatible value comparers ("No coercion operator … Guid and WorkOrderId").
        var keys = ids.Select(WorkOrderId.From).ToList();
        return await context.WorkOrders
            .Include("_serviceLines")
            .Include("_tasks._employees")
            .Include("_tasks._tools")
            .Include("_tasks._materials")
            .Include("_tasks._generalSupports")
            .Include("_tasks._attachments")
            .Where(w => keys.Contains(w.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkOrderEntity>> GetByFlightIdAsync(
        OpsFlight.FlightId flightId,
        CancellationToken cancellationToken = default) =>
        await context.WorkOrders
            .Include("_serviceLines")
            .Include("_tasks._employees")
            .Include("_tasks._tools")
            .Include("_tasks._materials")
            .Include("_tasks._generalSupports")
            .Include("_tasks._attachments")
            .Where(w => w.FlightId == flightId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkOrderEntity>> GetDueForDeletionAsync(
        DateTimeOffset threshold,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await context.WorkOrders
            .Where(w => w.Status == WorkOrderStatus.Deleting
                        && w.MarkedForDeletionAt != null
                        && w.MarkedForDeletionAt <= threshold)
            .OrderBy(w => w.MarkedForDeletionAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public void Add(WorkOrderEntity workOrder) => context.WorkOrders.Add(workOrder);

    /// <summary>
    /// Same rationale as <see cref="FlightRepository.Update"/>: callers load through
    /// <see cref="GetByIdAsync"/> and mutate in place — <c>Update</c> on an already-tracked
    /// graph incorrectly marks nested children added in memory as Modified.
    /// </summary>
    public void Update(WorkOrderEntity workOrder)
    {
        if (context.Entry(workOrder).State == EntityState.Detached)
            context.WorkOrders.Update(workOrder);
    }

    public void Remove(WorkOrderEntity workOrder) => context.WorkOrders.Remove(workOrder);
}
