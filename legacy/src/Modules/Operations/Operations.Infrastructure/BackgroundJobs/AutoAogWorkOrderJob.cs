using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Operations.Application.Abstractions;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;
using Operations.Domain.Enumerations;
using Quartz;

namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Quartz job that auto-issues a basic work order for an AOG flight whose scheduled
/// departure elapsed by <see cref="AutoAogWorkOrderSettings.DelayMinutes"/> while the
/// flight is still in <see cref="FlightStatus.Scheduled"/> — i.e. no employee has acted
/// on it (claimed, invited, created a work order). The auto-issued work order copies the
/// flight number, aircraft type and schedule from the flight, seeds ATA/ATD with the
/// flight's STA/STD, and carries no service lines or tasks. Attaching it flips the
/// flight to <see cref="FlightStatus.InProgress"/> via <c>Flight.AttachWorkOrder</c>.
/// </summary>
/// <remarks>
/// The job dispatches <see cref="CreateWorkOrderForFlightCommand"/> through MediatR for
/// each candidate so the full pipeline runs — transaction, mobile-sync flush, etc. —
/// exactly like a portal / mobile-originated submission. Each command runs in its own
/// MediatR scope; one failing flight does not stop the rest of the batch.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class AutoAogWorkOrderJob(
    IOperationsDbContext db,
    ISender mediator,
    IOptionsMonitor<AutoAogWorkOrderSettings> settings,
    ILogger<AutoAogWorkOrderJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var snapshot = settings.CurrentValue;
        if (!snapshot.Enabled)
            return;

        var delay = TimeSpan.FromMinutes(Math.Max(0, snapshot.DelayMinutes));
        var threshold = DateTimeOffset.UtcNow - delay;
        var batchSize = Math.Max(1, snapshot.BatchSize);

        List<CandidateFlight> candidates;
        try
        {
            // Only Scheduled AOG flights past the configured grace window are eligible.
            // Status==Scheduled implicitly excludes flights that already have any attached
            // work order (AttachWorkOrder transitions Scheduled -> InProgress), so no
            // separate "has no work order" guard is needed.
            candidates = await db.Flights
                .Where(f => f.Status == FlightStatus.Scheduled)
                .Where(f => f.Schedule.Std <= threshold)
                .Where(f => f.Services.Any(s => s.Service.IsAog))
                .OrderBy(f => f.Schedule.Std)
                .Take(batchSize)
                .Select(f => new CandidateFlight(
                    f.Id.Value,
                    f.FlightNumber.Value,
                    f.AircraftType == null ? (Guid?)null : f.AircraftType.AircraftTypeId,
                    f.Schedule.Sta,
                    f.Schedule.Std))
                .ToListAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoAogWorkOrder: failed to query candidate AOG flights");
            return;
        }

        if (candidates.Count == 0)
            return;

        var issued = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var command = new CreateWorkOrderForFlightCommand(
                    FlightId: candidate.FlightId,
                    FlightNumber: candidate.FlightNumber,
                    AircraftTypeId: candidate.AircraftTypeId,
                    AircraftTailNumber: null,
                    IsCanceled: false,
                    CancellationAt: null,
                    Ata: candidate.Sta,
                    Atd: candidate.Std,
                    ServiceLines: null,
                    Tasks: null,
                    Remarks: null,
                    CreatedByEmployeeId: null,
                    CustomerSignature: null);

                var result = await mediator.Send(command, context.CancellationToken);
                if (result.IsSuccess)
                {
                    issued++;
                    logger.LogInformation(
                        "AutoAogWorkOrder: issued work order {WorkOrderId} for AOG flight {FlightId}",
                        result.Value!.WorkOrderId, candidate.FlightId);
                }
                else
                {
                    // Conflicts ("already has accepted work order", etc.) are expected race
                    // outcomes — log at Warning so they're visible but don't drown the log.
                    logger.LogWarning(
                        "AutoAogWorkOrder: skipped flight {FlightId}: {Error}",
                        candidate.FlightId, result.Error.Description);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "AutoAogWorkOrder: failed to auto-issue work order for flight {FlightId}",
                    candidate.FlightId);
            }
        }

        if (issued > 0)
            logger.LogInformation("AutoAogWorkOrder: auto-issued {Count} work order(s)", issued);
    }

    private sealed record CandidateFlight(
        Guid FlightId,
        string FlightNumber,
        Guid? AircraftTypeId,
        DateTimeOffset Sta,
        DateTimeOffset Std);
}
