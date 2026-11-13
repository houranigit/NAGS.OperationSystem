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
        await PreRtrMigrationSeed.InsertFlightOwnerAsync(db, flight, includesReturnToRampSequence: true);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Submission_correction_and_its_event_commit_together_or_roll_back_on_stale_flight(bool departure, bool stale)
    {
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.MigrateAsync();
        var flight = CreateFlight();
        var editor = flight.AssignedEmployees.Single().Employee;
        var author = new StaffMemberSnapshot(Guid.NewGuid(), "Original submitter", "E2");
        var authorUserId = Guid.NewGuid();
        var editorUserId = Guid.NewGuid();
        var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var arrivalDetails = ArrivalDetails.Create("REF", Now, aircraft, "HZ-TEST", author, "Original").Value;
        var signature = CustomerSignature.Create("customer.png", "private-signatures/customer.png", "image/png", 100, Now).Value;
        var departureDetails = DepartureDetails.Create(Now.AddHours(2), signature, author, "Original").Value;
        var arrival = flight.SubmitArrival(author, arrivalDetails, authorUserId, Now).Value;
        var departed = flight.SubmitDeparture(author, departureDetails, authorUserId, Now).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var version = flight.RowVersion.ToArray();

        if (stale)
        {
            await using var competingScope = services.CreateAsyncScope();
            var competing = competingScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var winner = await competing.Flights.SingleAsync();
            winner.ChangeFlightNumber(FlightNumber.Create("721", "SV").Value, Now).IsSuccess.ShouldBeTrue();
            await competing.SaveChangesAsync();
        }

        // The correction uses the same timestamp as creation. Modified owned details must
        // still check the aggregate rowversion before their outbox envelope can commit.
        var submissionId = departure ? departed.Id : arrival.Id;
        if (departure)
            flight.SubmitDeparture(editor, DepartureDetails.Create(Now.AddHours(2), signature, author, "Corrected").Value,
                editorUserId, Now, submissionId).IsSuccess.ShouldBeTrue();
        else
            flight.SubmitArrival(editor, ArrivalDetails.Create("REF", Now, aircraft, "HZ-TEST", author, "Corrected").Value,
                editorUserId, Now, submissionId).IsSuccess.ShouldBeTrue();
        var updated = new FlightSubmissionUpdated
        {
            FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = submissionId,
            SubmissionKind = departure ? FlightSubmissionKind.Departure : FlightSubmissionKind.Arrival,
            SubmittedByStaffMemberId = author.StaffMemberId, SubmittedByUserId = authorUserId,
            UpdatedByStaffMemberId = editor.StaffMemberId, UpdatedByUserId = editorUserId,
            UpdatedByDisplayName = editor.FullName, OccurredOnUtc = Now
        };
        db.OutboxMessages.Add(OutboxMessage.Create(updated));

        if (stale)
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        else
            await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.Flights.Include(item => item.ArrivalSubmissions).Include(item => item.DepartureSubmissions).SingleAsync();
        var arrivalSaved = persisted.ArrivalSubmissions.Single();
        var departureSaved = persisted.DepartureSubmissions.Single();
        (departure ? departureSaved.Details.Remarks : arrivalSaved.Details.Remarks).ShouldBe(stale ? "Original" : "Corrected");
        arrivalSaved.SubmittedByEmployee.ShouldBe(author);
        departureSaved.SubmittedByEmployee.ShouldBe(author);
        arrivalSaved.CreatedByUserId.ShouldBe(authorUserId);
        departureSaved.CreatedByUserId.ShouldBe(authorUserId);
        (await db.OutboxMessages.CountAsync()).ShouldBe(stale ? 0 : 1);
        if (!stale)
        {
            persisted.RowVersion.ShouldNotBe(version);
            (await db.OutboxMessages.SingleAsync()).Id.ShouldBe(updated.EventId);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Work_correction_and_its_notification_commit_or_roll_back_together(bool task, bool stale, bool delete)
    {
        await using var services = fixture.CreateServices(); await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>(); await db.Database.MigrateAsync();
        var flight = CreateFlight(); var editor = flight.AssignedEmployees.Single().Employee;
        var creatorUserId = Guid.NewGuid(); var editorUserId = Guid.NewGuid(); var id = Guid.NewGuid();
        var window = TimeWindow.Create(Now, null).Value;
        var performerId = Guid.NewGuid();
        var selectedService = new ServiceSnapshot(Guid.NewGuid(), "GPU");
        if (task)
            flight.SaveTask(id, FlightTaskType.Major, null, null, window, "Original",
                [new FlightTaskPerformerInput(editor, window, null, performerId)], [], [], [], creatorUserId, Now).IsSuccess.ShouldBeTrue();
        else
            flight.SaveService(id, selectedService, null, window, "Original",
                [new FlightServicePerformerInput(editor, window, performerId)], creatorUserId, Now).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight); await db.SaveChangesAsync();
        if (stale)
        {
            await using var otherScope = services.CreateAsyncScope();
            var other = otherScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var winner = await other.Flights.SingleAsync();
            winner.ChangeFlightNumber(FlightNumber.Create("721", "SV").Value, Now).IsSuccess.ShouldBeTrue(); await other.SaveChangesAsync();
        }
        // Equal timestamps must still force the owned graph's aggregate concurrency check.
        if (delete)
        {
            if (task) flight.RemoveTask(id, editorUserId, Now, assignedEditorStaffMemberId: editor.StaffMemberId).IsSuccess.ShouldBeTrue();
            else flight.RemoveService(id, editorUserId, Now, assignedEditorStaffMemberId: editor.StaffMemberId).IsSuccess.ShouldBeTrue();
        }
        else if (task)
            flight.SaveTask(id, FlightTaskType.Minor, null, null, window, "Corrected",
                [new FlightTaskPerformerInput(editor, window, "Staff correction", performerId)], [], [], [], editorUserId, Now).IsSuccess.ShouldBeTrue();
        else
            flight.SaveService(id, selectedService, null, window, "Corrected",
                [new FlightServicePerformerInput(editor, window, performerId)], editorUserId, Now, editor.StaffMemberId).IsSuccess.ShouldBeTrue();
        db.OutboxMessages.Add(OutboxMessage.Create(new FlightSubmissionUpdated
        {
            FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = id,
            SubmissionKind = task ? FlightSubmissionKind.Task : FlightSubmissionKind.Service,
            Action = delete ? FlightSubmissionAction.Deleted : FlightSubmissionAction.Updated,
            SubmittedByUserId = creatorUserId, SubmittedByStaffMemberId = null,
            UpdatedByStaffMemberId = editor.StaffMemberId, UpdatedByUserId = editorUserId,
            UpdatedByDisplayName = editor.FullName, OccurredOnUtc = Now
        }));
        if (stale) await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        else await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var retained = await db.Flights.Include(item => item.ServiceRecords).ThenInclude(item => item.Performers)
            .Include(item => item.TaskRecords).ThenInclude(item => item.Performers).SingleAsync();
        if (delete && !stale) (task ? retained.Tasks.Count : retained.Services.Count).ShouldBe(0);
        else
        {
            (task ? retained.Tasks.Single().Notes : retained.Services.Single().Notes).ShouldBe(stale ? "Original" : "Corrected");
            (task ? retained.Tasks.Single().CreatedByUserId : retained.Services.Single().CreatedByUserId).ShouldBe(creatorUserId);
        }
        (await db.OutboxMessages.CountAsync()).ShouldBe(stale ? 0 : 1);
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
