using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Infrastructure.Persistence;
using Quartz;

namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Quartz job that hard-deletes work orders whose <see cref="Operations.Domain.Aggregates.WorkOrder.WorkOrder.MarkedForDeletionAt"/>
/// is older than the configured <see cref="WorkOrderDeletionSettings.DelayMinutes"/>.
/// Also detaches each removed work order from its flight so the flight's
/// <c>AttachedWorkOrders</c> list stays consistent.
/// </summary>
[DisallowConcurrentExecution]
public sealed class WorkOrderDeletionJob(
    IWorkOrderRepository workOrders,
    IFlightRepository flights,
    OperationsDbContext db,
    IOptionsMonitor<WorkOrderDeletionSettings> settings,
    ILogger<WorkOrderDeletionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var snapshot = settings.CurrentValue;
        var delay = TimeSpan.FromMinutes(Math.Max(0, snapshot.DelayMinutes));
        var threshold = DateTimeOffset.UtcNow - delay;
        var batchSize = Math.Max(1, snapshot.BatchSize);

        IReadOnlyList<Operations.Domain.Aggregates.WorkOrder.WorkOrder> due;
        try
        {
            due = await workOrders.GetDueForDeletionAsync(threshold, batchSize, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WorkOrderDeletion: failed to query due work orders");
            return;
        }

        if (due.Count == 0)
            return;

        foreach (var workOrder in due)
        {
            try
            {
                if (workOrder.FlightId is not null)
                {
                    var flight = await flights.GetByIdAsync(workOrder.FlightId, context.CancellationToken);
                    if (flight is not null)
                    {
                        // Detach is best-effort: a flight may have already been settled by another
                        // approval that re-attached only the winning work order.
                        _ = flight.DetachWorkOrder(workOrder.Id);
                        flights.Update(flight);
                    }
                }

                workOrders.Remove(workOrder);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WorkOrderDeletion: failed to remove work order {WorkOrderId}", workOrder.Id.Value);
            }
        }

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation("WorkOrderDeletion: removed {Count} work orders", due.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WorkOrderDeletion: failed to commit deletion batch");
        }
    }
}
