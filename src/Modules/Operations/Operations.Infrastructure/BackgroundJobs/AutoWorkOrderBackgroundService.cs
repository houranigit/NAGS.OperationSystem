using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;

namespace Operations.Infrastructure.BackgroundJobs;

public sealed class AutoWorkOrderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AutoWorkOrderOptions> options,
    TimeProvider timeProvider,
    ILogger<AutoWorkOrderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto work-order scan failed.");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollIntervalSeconds, 30));
            await Task.Delay(delay, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddMinutes(-Math.Max(settings.DelayMinutes, 1));
        var batchSize = Math.Clamp(settings.BatchSize, 1, 100);

        var flights = await db.Flights
            .Include(f => f.PlannedServices)
            .Where(f => f.Status == FlightStatus.Scheduled)
            .Where(f => f.Schedule.Std <= cutoff)
            .Where(f => f.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
            .OrderBy(f => f.Schedule.Std)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var flight in flights)
        {
            var hasOpenWorkOrder = await db.WorkOrders.AsNoTracking().AnyAsync(w =>
                w.FlightId == flight.Id && w.Status != WorkOrderStatus.Merged,
                cancellationToken);
            if (hasOpenWorkOrder)
                continue;

            var workOrder = WorkOrder.SubmitNew(
                flight,
                WorkOrderType.Completion,
                Guid.Empty,
                owner: null,
                actualFlightNumber: null,
                aircraftType: null,
                aircraftTailNumber: null,
                actuals: null,
                cancellation: null,
                remarks: "Automatically generated for overdue Per-Landing review.",
                serviceLines: [],
                tasks: [],
                now);
            if (workOrder.IsFailure)
            {
                logger.LogWarning("Could not auto-create work order for flight {FlightId}: {Code}", flight.Id, workOrder.Error.Code);
                continue;
            }

            var transition = flight.OnWorkOrderSubmitted(now);
            if (transition.IsFailure)
            {
                logger.LogWarning("Could not move flight {FlightId} to InProgress for auto work order: {Code}", flight.Id, transition.Error.Code);
                continue;
            }

            db.WorkOrders.Add(workOrder.Value);
            db.WorkOrderTimelineEntries.Add(new WorkOrderTimelineEntry(
                workOrder.Value.Id,
                WorkOrderTimelineEventType.Submitted,
                now,
                Guid.Empty,
                "System",
                "Automatically generated for overdue Per-Landing review."));
            db.FlightTimelineEntries.Add(new Domain.Flights.FlightTimelineEntry(
                flight.Id,
                FlightTimelineEventType.WorkOrderSubmitted,
                now,
                Guid.Empty,
                "System",
                $"Auto-generated work order {workOrder.Value.Id}."));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Auto work-order creation conflicted for flight {FlightId}.", flight.Id);
            }
        }
    }
}
