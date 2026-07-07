using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Quartz;

namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Auto-generates a submitted, empty completion work order for Per-Landing flights that are still
/// Scheduled past STD + a configured delay (default 60 min), so they enter the review queue instead of
/// staying open forever. Does not auto-approve; an admin still reviews and approves.
/// </summary>
[DisallowConcurrentExecution]
public sealed class AutoWorkOrderJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AutoWorkOrderJob> logger) : IJob
{
    public static readonly JobKey Key = new("operations-auto-work-order");

    public async Task Execute(IJobExecutionContext context)
    {
        var delayMinutes = configuration.GetValue("Operations:AutoWorkOrder:DelayMinutes", 60);
        var batchSize = configuration.GetValue("Operations:AutoWorkOrder:BatchSize", 50);
        var now = timeProvider.GetUtcNow();
        var threshold = now.AddMinutes(-delayMinutes);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();

        var candidates = await db.Flights
            .Where(f => f.Status == FlightStatus.Scheduled)
            .Where(f => f.Schedule.Std <= threshold)
            .Where(f => f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
            .Where(f => !db.WorkOrders.Any(w => w.FlightId == f.Id))
            .OrderBy(f => f.Schedule.Std)
            .Take(batchSize)
            .ToListAsync(context.CancellationToken);

        foreach (var flight in candidates)
        {
            var workOrder = WorkOrder.OpenCompletion(
                new FlightContext(flight.Id, flight.Customer, flight.Station, flight.OperationType, flight.FlightNumber, flight.Schedule, flight.AircraftType),
                createdByUserId: Guid.Empty, owner: null, now);

            var actuals = ActualTime.Create(flight.Schedule.Sta, flight.Schedule.Std);
            if (actuals.IsSuccess)
                workOrder.SetActualTimes(actuals.Value, now);

            flight.OnWorkOrderSubmitted(now);
            db.WorkOrders.Add(workOrder);
            db.FlightTimelineEntries.Add(new Operations.Domain.Flights.FlightTimelineEntry(
                flight.Id, FlightTimelineEventType.WorkOrderSubmitted, now, actorUserId: Guid.Empty, actorName: null, workOrder.Id));
            db.WorkOrderTimelineEntries.Add(new WorkOrderTimelineEntry(
                workOrder.Id, flight.Id, WorkOrderTimelineEventType.Submitted, now, actorUserId: Guid.Empty, actorName: null));
        }

        if (candidates.Count > 0)
        {
            await db.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation("Auto-generated {Count} Per-Landing work orders.", candidates.Count);
        }
    }
}
