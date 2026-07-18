using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Operations.Contracts;
using Operations.Domain.Enumerations;
using Operations.Infrastructure.Persistence;

namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Enrolls upcoming flight assignments into a durable three-milestone schedule, then moves due
/// schedules into the Operations outbox. Schedule state and its outbox event are saved atomically.
/// </summary>
public sealed class FlightReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<FlightReminderOptions> options,
    TimeProvider timeProvider,
    ILogger<FlightReminderBackgroundService> logger) : BackgroundService
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
            catch (Exception exception)
            {
                logger.LogError(exception, "Flight reminder scan failed.");
            }

            var delay = TimeSpan.FromSeconds(Math.Clamp(
                options.CurrentValue.PollIntervalSeconds,
                FlightReminderOptions.MinimumPollIntervalSeconds,
                FlightReminderOptions.MaximumPollIntervalSeconds));
            await Task.Delay(delay, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
            return;

        var now = timeProvider.GetUtcNow();
        await CleanupTerminalSchedulesAsync(settings, now, cancellationToken);
        await EnrollUpcomingAssignmentsAsync(settings, now, cancellationToken);
        await DispatchDueRemindersAsync(settings, now, cancellationToken);
    }

    private async Task CleanupTerminalSchedulesAsync(
        FlightReminderOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var retentionDays = Math.Clamp(
            settings.TerminalRetentionDays,
            FlightReminderOptions.MinimumTerminalRetentionDays,
            FlightReminderOptions.MaximumTerminalRetentionDays);
        var batchSize = Math.Clamp(settings.CleanupBatchSize, 1, FlightReminderOptions.MaximumBatchSize);
        var cutoff = now.AddDays(-retentionDays);

        var expired = await db.FlightReminderSchedules
            .Where(reminder =>
                (reminder.State == FlightReminderState.Dispatched && reminder.DispatchedAtUtc <= cutoff) ||
                (reminder.State == FlightReminderState.Skipped && reminder.SkippedAtUtc <= cutoff))
            .OrderBy(reminder => reminder.DispatchedAtUtc ?? reminder.SkippedAtUtc)
            .ThenBy(reminder => reminder.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return;

        db.FlightReminderSchedules.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnrollUpcomingAssignmentsAsync(
        FlightReminderOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var horizon = now.AddHours(Math.Clamp(
            settings.EnrollmentLookaheadHours,
            FlightReminderOptions.MinimumEnrollmentLookaheadHours,
            FlightReminderOptions.MaximumEnrollmentLookaheadHours));
        var batchSize = Math.Clamp(settings.EnrollmentBatchSize, 1, FlightReminderOptions.MaximumBatchSize);

        // Select only assignments with an incomplete schedule set. Without this correlated filter,
        // the earliest fully-enrolled batch would permanently hide later flights from Take().
        var candidates = await (
                from flight in db.Flights.AsNoTracking()
                from assignment in flight.AssignedEmployees
                where flight.Status == FlightStatus.Scheduled || flight.Status == FlightStatus.InProgress
                where flight.Schedule.Sta > now && flight.Schedule.Sta <= horizon
                where db.FlightReminderSchedules.Count(reminder =>
                    reminder.FlightId == flight.Id &&
                    reminder.StaffMemberId == assignment.Employee.StaffMemberId &&
                    reminder.ScheduledArrivalUtc == flight.Schedule.Sta) < FlightReminderLeadTimes.Count
                orderby flight.Schedule.Sta, flight.Id, assignment.Employee.StaffMemberId
                select new EnrollmentCandidate(
                    flight.Id,
                    assignment.Employee.StaffMemberId,
                    flight.Schedule.Sta))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return;

        var flightIds = candidates.Select(candidate => candidate.FlightId).Distinct().ToList();
        var existing = await db.FlightReminderSchedules.AsNoTracking()
            .Where(reminder => flightIds.Contains(reminder.FlightId))
            .Where(reminder => reminder.ScheduledArrivalUtc > now && reminder.ScheduledArrivalUtc <= horizon)
            .Select(reminder => new
            {
                reminder.FlightId,
                reminder.StaffMemberId,
                reminder.ScheduledArrivalUtc,
                reminder.LeadTimeMinutes
            })
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(reminder => new ScheduleKey(
                reminder.FlightId,
                reminder.StaffMemberId,
                reminder.ScheduledArrivalUtc,
                reminder.LeadTimeMinutes))
            .ToHashSet();
        var latenessTolerance = TimeSpan.FromMinutes(Math.Clamp(
            settings.EnrollmentLatenessToleranceMinutes,
            0,
            FlightReminderOptions.MaximumEnrollmentLatenessToleranceMinutes));

        foreach (var candidate in candidates)
        {
            foreach (var leadTimeMinutes in FlightReminderLeadTimes.Descending)
            {
                var key = new ScheduleKey(
                    candidate.FlightId,
                    candidate.StaffMemberId,
                    candidate.ScheduledArrivalUtc,
                    leadTimeMinutes);
                if (!existingKeys.Add(key))
                    continue;

                db.FlightReminderSchedules.Add(FlightReminderSchedule.Create(
                    candidate.FlightId,
                    candidate.StaffMemberId,
                    candidate.ScheduledArrivalUtc,
                    leadTimeMinutes,
                    now,
                    latenessTolerance));
            }
        }

        if (!db.ChangeTracker.HasChanges())
            return;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Another host may have enrolled the same unique flight/staff/STA/milestone set. Its
            // committed rows are authoritative; a fresh dispatch scope below can process them.
            logger.LogWarning(exception, "Flight reminder enrollment conflicted with another scanner.");
        }
    }

    private async Task DispatchDueRemindersAsync(
        FlightReminderOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var batchSize = Math.Clamp(settings.DispatchBatchSize, 1, FlightReminderOptions.MaximumBatchSize);
        var reminders = await db.FlightReminderSchedules
            .Where(reminder => reminder.State == FlightReminderState.Pending && reminder.DueAtUtc <= now)
            .OrderByDescending(reminder => reminder.DueAtUtc)
            .ThenBy(reminder => reminder.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (reminders.Count == 0)
            return;

        var flightIds = reminders.Select(reminder => reminder.FlightId).Distinct().ToList();
        var flights = await db.Flights.AsNoTracking()
            .Include(flight => flight.AssignedEmployees)
            .Where(flight => flightIds.Contains(flight.Id))
            .ToDictionaryAsync(flight => flight.Id, cancellationToken);

        foreach (var reminder in reminders)
        {
            if (!flights.TryGetValue(reminder.FlightId, out var flight))
            {
                reminder.MarkSkipped(now, "Flight no longer exists.");
                continue;
            }

            if (flight.Status is not (FlightStatus.Scheduled or FlightStatus.InProgress))
            {
                reminder.MarkSkipped(now, $"Flight is {flight.Status}.");
                continue;
            }

            if (flight.Schedule.Sta != reminder.ScheduledArrivalUtc)
            {
                reminder.MarkSkipped(now, "Flight STA changed after reminder enrollment.");
                continue;
            }

            if (!flight.AssignedEmployees.Any(assignment =>
                    assignment.Employee.StaffMemberId == reminder.StaffMemberId))
            {
                reminder.MarkSkipped(now, "Employee is no longer assigned to the flight.");
                continue;
            }

            var currentLeadTime = FlightReminderLeadTimes.CurrentFor(flight.Schedule.Sta, now);
            if (currentLeadTime is null)
            {
                reminder.MarkSkipped(now, "Flight is outside the reminder window.");
                continue;
            }

            if (reminder.LeadTimeMinutes != currentLeadTime.Value)
            {
                // Older due milestones are superseded by the closest currently applicable one.
                // Future milestones are not loaded because their DueAtUtc is still ahead of now.
                reminder.MarkSkipped(now, "A closer reminder milestone is now applicable.");
                continue;
            }

            reminder.MarkDispatched(now);
            db.Enqueue(new FlightReminderDue
            {
                EventId = reminder.Id,
                FlightId = flight.Id,
                FlightNumber = flight.FlightNumber.Value,
                StaffMemberId = reminder.StaffMemberId,
                ScheduledArrivalUtc = flight.Schedule.Sta,
                LeadTimeMinutes = reminder.LeadTimeMinutes,
                OccurredOnUtc = now
            });
        }

        if (!db.ChangeTracker.HasChanges())
            return;

        try
        {
            // EF wraps the state transitions and outbox inserts in one transaction. A crash cannot
            // leave a reminder marked dispatched without its durable notification event.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The outbox primary key is the stable schedule id. Concurrent scanners attempting the
            // same dispatch race on that key, and the losing transaction rolls back every state edit.
            logger.LogWarning(exception, "Flight reminder dispatch conflicted with another scanner.");
        }
    }

    private sealed record EnrollmentCandidate(
        Guid FlightId,
        Guid StaffMemberId,
        DateTimeOffset ScheduledArrivalUtc);

    private readonly record struct ScheduleKey(
        Guid FlightId,
        Guid StaffMemberId,
        DateTimeOffset ScheduledArrivalUtc,
        int LeadTimeMinutes);
}
