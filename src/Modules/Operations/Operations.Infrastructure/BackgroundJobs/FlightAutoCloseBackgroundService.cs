using BuildingBlocks.Application.Mobile;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Infrastructure.Persistence;

namespace Operations.Infrastructure.BackgroundJobs;

public sealed class FlightAutoCloseBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<FlightAutoCloseBackgroundService> logger) : BackgroundService
{
    public const string EnabledKey = "Operations:FlightAutoClose:Enabled";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

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
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Per Landing auto-close scan failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - Flight.AutoCloseDelay;
        await using var scanScope = scopeFactory.CreateAsyncScope();
        var scanDb = scanScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var candidates = await EligibleFlights(scanDb, cutoff)
            .OrderBy(flight => flight.Schedule.Std).ThenBy(flight => flight.Id)
            .Select(flight => flight.Id)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var flightId in candidates)
        {
            await using var writeScope = scopeFactory.CreateAsyncScope();
            var db = writeScope.ServiceProvider.GetRequiredService<OperationsDbContext>();

            // Verify that the operational collections are empty in SQL. The flight rowversion
            // also checks for any operational write committed after this read.
            var flight = await EligibleFlights(db, cutoff).Include(item => item.PlannedServices)
                .SingleOrDefaultAsync(item => item.Id == flightId, cancellationToken);
            if (flight is null)
                continue;

            var sequence = await FlightApprovalNumbers.FindAvailableSequenceAsync(db, flight.Station.StationId, cancellationToken);
            var result = sequence.IsFailure ? sequence.Error : flight.AutoClose(now, sequence.Value);
            if (result.IsFailure)
            {
                logger.LogWarning("Could not auto-close Per Landing flight {FlightId}: {Code}.", flightId, result.Error.Code);
                continue;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                changed = true;
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogDebug("Per Landing flight {FlightId} or its work-order sequence changed during auto-close.", flightId);
            }
        }

        if (changed)
        {
            if (scanScope.ServiceProvider.GetService<IOperationsDashboardRealtimeNotifier>() is { } dashboard)
            {
                try
                {
                    await dashboard.NotifyChangedAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Could not refresh the operations dashboard after auto-close.");
                }
            }

            var mobileSync = scanScope.ServiceProvider.GetRequiredService<IMobileSyncBroadcaster>();
            await mobileSync.BroadcastNowAsync(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh,
                null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()), cancellationToken);
        }
    }

    private static IQueryable<Flight> EligibleFlights(OperationsDbContext db, DateTimeOffset cutoff) =>
        db.Flights.Where(flight => flight.ApprovalStatus == FlightApprovalStatus.NotApproved &&
            flight.Schedule.Std <= cutoff && flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
            flight.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
            !flight.ArrivalSubmissions.Any() && !flight.DepartureSubmissions.Any() && !flight.CancellationSubmissions.Any() &&
            !flight.ServiceRecords.Any() && !flight.TaskRecords.Any() && !flight.ReturnToRamps.Any());
}
