using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.BackgroundJobs;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightReminderBackgroundServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Run_once_dispatches_each_due_milestone_only_once_across_restarts()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight(Now.AddHours(12));
        await SeedAsync(services, flight);

        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(10));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(90));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var schedules = await db.FlightReminderSchedules.OrderBy(row => row.LeadTimeMinutes).ToListAsync();
        schedules.Count.ShouldBe(3);
        schedules.ShouldAllBe(row => row.State == FlightReminderState.Dispatched);

        var messages = await db.OutboxMessages.ToListAsync();
        messages.Count.ShouldBe(3);
        var reminders = messages
            .Select(message => JsonSerializer.Deserialize<FlightReminderDue>(message.Content)!)
            .ToList();
        reminders.Select(reminder => reminder.LeadTimeMinutes).ToHashSet()
            .SetEquals(FlightReminderLeadTimes.All).ShouldBeTrue();
        reminders.ShouldAllBe(reminder => reminder.FlightId == flight.Id);
        reminders.ShouldAllBe(reminder =>
            reminder.StaffMemberId == flight.AssignedEmployees.Single().Employee.StaffMemberId);
        reminders.ShouldAllBe(reminder => reminder.ScheduledArrivalUtc == flight.Schedule.Sta);
        messages.ShouldAllBe(message => reminders.Any(reminder => reminder.EventId == message.Id));
    }

    [Fact]
    public async Task Late_enrollment_skips_old_milestones_and_dispatches_only_current_threshold()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight(Now.AddHours(1).AddMinutes(58));
        await SeedAsync(services, flight);

        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var schedules = await db.FlightReminderSchedules.ToListAsync();
        schedules.Single(row => row.LeadTimeMinutes == FlightReminderLeadTimes.TwelveHours)
            .State.ShouldBe(FlightReminderState.Skipped);
        schedules.Single(row => row.LeadTimeMinutes == FlightReminderLeadTimes.TwoHours)
            .State.ShouldBe(FlightReminderState.Dispatched);
        schedules.Single(row => row.LeadTimeMinutes == FlightReminderLeadTimes.ThirtyMinutes)
            .State.ShouldBe(FlightReminderState.Pending);

        var events = await db.OutboxMessages.Select(message => message.Content).ToListAsync();
        events.Count.ShouldBe(1);
        JsonSerializer.Deserialize<FlightReminderDue>(events[0])!.LeadTimeMinutes
            .ShouldBe(FlightReminderLeadTimes.TwoHours);
    }

    [Fact]
    public async Task Due_reminder_is_skipped_when_employee_was_unassigned_after_enrollment()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight(Now.AddHours(13));
        await SeedAsync(services, flight);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using (var mutationScope = services.CreateAsyncScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var tracked = await db.Flights
                .Include(item => item.AssignedEmployees)
                .Include(item => item.PlannedServices)
                .SingleAsync(item => item.Id == flight.Id);
            tracked.ReplaceAssignedEmployees([], time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromHours(1));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var twelveHour = await verification.FlightReminderSchedules.SingleAsync(
            row => row.LeadTimeMinutes == FlightReminderLeadTimes.TwelveHours);
        twelveHour.State.ShouldBe(FlightReminderState.Skipped);
        twelveHour.SkipReason.ShouldNotBeNull();
        twelveHour.SkipReason!.ShouldContain("no longer assigned");
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Due_reminder_is_skipped_when_sta_changed_after_enrollment()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var originalSta = Now.AddHours(13);
        var flight = CreateFlight(originalSta);
        await SeedAsync(services, flight);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using (var mutationScope = services.CreateAsyncScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var tracked = await db.Flights
                .Include(item => item.AssignedEmployees)
                .Include(item => item.PlannedServices)
                .SingleAsync(item => item.Id == flight.Id);
            var changed = ScheduledTime.Create(originalSta.AddHours(1), originalSta.AddHours(3));
            tracked.UpdateSchedule(changed.Value, aircraftType: null, time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromHours(1));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var oldTwelveHour = await verification.FlightReminderSchedules.SingleAsync(row =>
            row.ScheduledArrivalUtc == originalSta &&
            row.LeadTimeMinutes == FlightReminderLeadTimes.TwelveHours);
        oldTwelveHour.State.ShouldBe(FlightReminderState.Skipped);
        oldTwelveHour.SkipReason.ShouldNotBeNull();
        oldTwelveHour.SkipReason!.ShouldContain("STA changed");
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Due_reminder_is_skipped_when_flight_is_terminal()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight(Now.AddHours(13));
        await SeedAsync(services, flight);
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using (var mutationScope = services.CreateAsyncScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var tracked = await db.Flights
                .Include(item => item.AssignedEmployees)
                .Include(item => item.PlannedServices)
                .SingleAsync(item => item.Id == flight.Id);
            tracked.SettleCanceled(time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromHours(1));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var twelveHour = await verification.FlightReminderSchedules.SingleAsync(
            row => row.LeadTimeMinutes == FlightReminderLeadTimes.TwelveHours);
        twelveHour.State.ShouldBe(FlightReminderState.Skipped);
        twelveHour.SkipReason.ShouldNotBeNull();
        twelveHour.SkipReason!.ShouldContain("Canceled");
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Reminder_that_becomes_due_after_sta_is_skipped_without_an_outbox_event()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight(Now.AddMinutes(20));
        await SeedAsync(services, flight);

        await using (var seedScope = services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.FlightReminderSchedules.Add(FlightReminderSchedule.Create(
                flight.Id,
                flight.AssignedEmployees.Single().Employee.StaffMemberId,
                flight.Schedule.Sta,
                FlightReminderLeadTimes.ThirtyMinutes,
                Now,
                TimeSpan.FromMinutes(60)));
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromMinutes(21));
        await NewWorker(services, time).RunOnceAsync(CancellationToken.None);

        await using var verificationScope = services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await verification.FlightReminderSchedules.SingleAsync()).State.ShouldBe(FlightReminderState.Skipped);
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Cleanup_removes_only_a_bounded_batch_of_expired_terminal_schedules()
    {
        await using var services = NewServices();
        var time = new MutableTimeProvider(Now);
        var oldDispatched = FlightReminderSchedule.Create(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-30).AddHours(13),
            FlightReminderLeadTimes.TwelveHours, Now.AddDays(-31), TimeSpan.Zero);
        oldDispatched.MarkDispatched(Now.AddDays(-31));
        var oldSkipped = FlightReminderSchedule.Create(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-32),
            FlightReminderLeadTimes.TwelveHours, Now.AddDays(-31), TimeSpan.Zero);
        var recentSkipped = FlightReminderSchedule.Create(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2),
            FlightReminderLeadTimes.TwelveHours, Now.AddDays(-1), TimeSpan.Zero);
        var pending = FlightReminderSchedule.Create(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1),
            FlightReminderLeadTimes.ThirtyMinutes, Now, TimeSpan.Zero);

        await using (var seedScope = services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.FlightReminderSchedules.AddRange(oldDispatched, oldSkipped, recentSkipped, pending);
            await db.SaveChangesAsync();
        }

        var settings = new FlightReminderOptions
        {
            TerminalRetentionDays = 30,
            CleanupBatchSize = 1
        };
        await NewWorker(services, time, settings).RunOnceAsync(CancellationToken.None);

        await using (var firstScope = services.CreateAsyncScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            (await db.FlightReminderSchedules.CountAsync()).ShouldBe(3);
        }

        await NewWorker(services, time, settings).RunOnceAsync(CancellationToken.None);

        await using var finalScope = services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var remaining = await finalDb.FlightReminderSchedules.ToListAsync();
        remaining.Count.ShouldBe(2);
        remaining.ShouldContain(row => row.Id == recentSkipped.Id);
        remaining.ShouldContain(row => row.Id == pending.Id);
    }

    private static ServiceProvider NewServices()
    {
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"flight-reminders-{Guid.NewGuid()}";
        services.AddDbContext<OperationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        return services.BuildServiceProvider();
    }

    private static FlightReminderBackgroundService NewWorker(
        ServiceProvider services,
        TimeProvider timeProvider,
        FlightReminderOptions? settings = null) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<FlightReminderOptions>(settings ?? new FlightReminderOptions()),
            timeProvider,
            NullLogger<FlightReminderBackgroundService>.Instance);

    private static async Task SeedAsync(ServiceProvider services, Flight flight)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
    }

    private static Flight CreateFlight(DateTimeOffset sta)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Reminder Recipient", "EMP-REM");
        return Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV720").Value,
            ScheduledTime.Create(sta, sta.AddHours(2)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: [employee],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
