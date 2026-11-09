using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.BackgroundJobs;
using Operations.Infrastructure.Persistence;
using Operations.Infrastructure.Readers;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightNotificationPersistenceTests(FlightNotificationPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightNotificationPersistenceTests.DatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Restore_migration_preserves_flights_rosters_versions_and_pending_assignment_events()
    {
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.MigrateAsync("20261109153847_Operations_UnifyCustomerSignatures");
        var flight = CreateFlight();
        var assignmentEvent = Assignment(flight);
        db.Flights.Add(flight);
        db.OutboxMessages.Add(OutboxMessage.Create(assignmentEvent));
        await db.SaveChangesAsync();
        var version = flight.RowVersion.ToArray();
        var rosterId = flight.AssignedEmployees.Single().Id;
        var plannedId = flight.PlannedServices.Single().Id;
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();

        var saved = await db.Flights.AsNoTracking()
            .Include(item => item.AssignedEmployees).Include(item => item.PlannedServices).SingleAsync();
        saved.Id.ShouldBe(flight.Id);
        saved.Schedule.ShouldBe(flight.Schedule);
        saved.RowVersion.ShouldBe(version);
        saved.AssignedEmployees.Single().Id.ShouldBe(rosterId);
        saved.PlannedServices.Single().Id.ShouldBe(plannedId);
        (await db.OutboxMessages.SingleAsync()).Id.ShouldBe(assignmentEvent.EventId);
        (await db.FlightReminderSchedules.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sql_scanner_persists_each_milestone_once_and_rejects_a_later_unaccepted_cancellation()
    {
        await using var services = fixture.CreateServices();
        var time = new MutableTimeProvider(Now);
        var flight = CreateFlight();
        await using (var seedScope = services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            await db.Database.MigrateAsync();
            db.Flights.Add(flight);
            await db.SaveChangesAsync();
        }

        FlightReminderBackgroundService Worker() => new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptionsMonitor<FlightReminderOptions>>(), time,
            NullLogger<FlightReminderBackgroundService>.Instance);

        await Worker().RunOnceAsync(CancellationToken.None);
        await Worker().RunOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(10));
        await Worker().RunOnceAsync(CancellationToken.None);

        await using (var mutationScope = services.CreateAsyncScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var tracked = await db.Flights.Include(item => item.AssignedEmployees)
                .Include(item => item.CancellationSubmissions).SingleAsync();
            tracked.SubmitCancellation(tracked.AssignedEmployees.Single().Employee,
                CancellationDetails.Create(time.GetUtcNow(), null).Value, Guid.NewGuid(), time.GetUtcNow())
                .IsSuccess.ShouldBeTrue();
            tracked.SubmitCancellation(new StaffMemberSnapshot(Guid.NewGuid(), "Other employee", "E2"),
                CancellationDetails.Create(time.GetUtcNow(), "Competing submission").Value, Guid.NewGuid(), time.GetUtcNow())
                .IsSuccess.ShouldBeTrue();
            tracked.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromMinutes(90));
        await Worker().RunOnceAsync(CancellationToken.None);
        await using var verificationScope = services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var schedules = await verification.FlightReminderSchedules.ToListAsync();
        schedules.Count.ShouldBe(3);
        schedules.Single(item => item.LeadTimeMinutes == 30).State.ShouldBe(FlightReminderState.Skipped);
        var envelopes = await verification.OutboxMessages.ToListAsync();
        envelopes.Count.ShouldBe(2);
        var reminders = envelopes.Select(item => JsonSerializer.Deserialize<FlightReminderDue>(item.Content)!).ToList();
        reminders.Select(item => item.LeadTimeMinutes).ShouldBe([720, 120], ignoreOrder: true);
        reminders.ShouldAllBe(item => schedules.Any(schedule => schedule.Id == item.EventId));
        var reader = new FlightReminderEligibilityReader(verification);
        (await reader.IsEligibleAsync(flight.Id, flight.AssignedEmployees.Single().Employee.StaffMemberId,
            flight.Schedule.Sta, time.GetUtcNow())).ShouldBeFalse();
    }

    [Fact]
    public async Task Sql_unique_milestone_identity_prevents_duplicate_schedules()
    {
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.MigrateAsync();
        var flight = CreateFlight();
        var staffId = flight.AssignedEmployees.Single().Employee.StaffMemberId;
        db.FlightReminderSchedules.Add(FlightReminderSchedule.Create(flight.Id, staffId, flight.Schedule.Sta, 720, Now, TimeSpan.Zero));
        await db.SaveChangesAsync();
        db.FlightReminderSchedules.Add(FlightReminderSchedule.Create(flight.Id, staffId, flight.Schedule.Sta, 720, Now, TimeSpan.Zero));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        (await db.FlightReminderSchedules.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Stale_roster_save_rolls_back_its_assignment_event()
    {
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.MigrateAsync();
        var flight = CreateFlight();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var originalStaffId = flight.AssignedEmployees.Single().Employee.StaffMemberId;

        await using (var concurrentScope = services.CreateAsyncScope())
        {
            var concurrent = concurrentScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var winner = await concurrent.Flights.SingleAsync();
            winner.ChangeFlightNumber(FlightNumber.Create("721", "SV").Value, Now).IsSuccess.ShouldBeTrue();
            await concurrent.SaveChangesAsync();
        }

        var added = new StaffMemberSnapshot(Guid.NewGuid(), "New employee", "E3");
        flight.ReplaceAssignedEmployees([added], Now).IsSuccess.ShouldBeTrue();
        db.OutboxMessages.Add(OutboxMessage.Create(Assignment(flight)));
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
        var saved = await db.Flights.Include(item => item.AssignedEmployees).SingleAsync();
        saved.FlightNumber.Value.ShouldBe("SV721");
        saved.AssignedEmployees.Single().Employee.StaffMemberId.ShouldBe(originalStaffId);
    }

    private static FlightEmployeeAssigned Assignment(Flight flight) => new()
    {
        FlightId = flight.Id,
        FlightNumber = flight.FlightNumber.Value,
        StaffMemberId = flight.AssignedEmployees.Single().Employee.StaffMemberId,
        OccurredOnUtc = Now
    };

    private static Flight CreateFlight() => Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
        new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
        FlightNumber.Create("720", "SV").Value,
        ScheduledTime.Create(Now.AddHours(12), Now.AddHours(14)).Value,
        null, [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
        [new StaffMemberSnapshot(Guid.NewGuid(), "Assigned employee", "E1")], Guid.NewGuid(), Now).Value;

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public ServiceProvider CreateServices()
        {
            var connection = new SqlConnectionStringBuilder(sql.GetConnectionString())
            {
                InitialCatalog = $"FlightNotifications_{Guid.NewGuid():N}"
            };
            var services = new ServiceCollection();
            services.AddDbContext<OperationsDbContext>(options => options.UseSqlServer(connection.ConnectionString,
                settings => settings.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema)));
            services.Configure<FlightReminderOptions>(_ => { });
            return services.BuildServiceProvider();
        }

        public async Task InitializeAsync() => await sql.StartAsync();
        public async Task DisposeAsync() => await sql.DisposeAsync();
    }
}
