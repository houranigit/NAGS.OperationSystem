using BuildingBlocks.Application.Messaging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightUserActivityPersistenceTests(FlightUserActivityPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightUserActivityPersistenceTests.DatabaseFixture>
{
    private const string PreviousMigration = "20261113172509_Operations_MobileAdHocCreation";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Activity_migration_backfills_only_retained_recording_evidence_without_inventing_intermediate_or_closer_times()
    {
        await using var db = fixture.CreateContext(); await db.Database.MigrateAsync(PreviousMigration);
        var author = Guid.NewGuid(); var actualEditor = Guid.NewGuid(); var serviceEditor = Guid.NewGuid(); var taskEditor = Guid.NewGuid();
        var rtrCreator = Guid.NewGuid(); var rtrEditor = Guid.NewGuid(); var cancelCreator = Guid.NewGuid(); var cancelEditor = Guid.NewGuid();
        var adHocCreator = Guid.NewGuid(); var planningCreator = Guid.NewGuid(); var lostIntermediate = Guid.NewGuid(); var closerWithoutRecordingTime = Guid.NewGuid();
        var receiving = Employee(); var editor = Employee(); var flight = FlightFor(planningCreator, [editor]);
        var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var arrival = flight.SubmitArrival(receiving, ArrivalDetails.Create(null, Now.AddHours(-12), aircraft, "HZ-A", receiving, "Original").Value,
            author, Now.AddHours(-10)).Value;
        flight.SubmitArrival(editor, ArrivalDetails.Create(null, arrival.Details.Ata, aircraft, "HZ-A", receiving, "Intermediate").Value,
            lostIntermediate, Now.AddHours(-9), arrival.Id).IsSuccess.ShouldBeTrue();
        flight.SubmitArrival(editor, ArrivalDetails.Create(null, arrival.Details.Ata, aircraft, "HZ-A", receiving, "Latest").Value,
            actualEditor, Now.AddHours(-8), arrival.Id).IsSuccess.ShouldBeTrue();
        var departure = flight.SubmitDeparture(receiving, DepartureDetails.Create(Now.AddHours(5), null, receiving, "Original").Value,
            author, Now.AddHours(-7)).Value;
        flight.SubmitDeparture(editor, DepartureDetails.Create(departure.Details.Atd, null, receiving, "Latest").Value,
            actualEditor, Now.AddHours(-6), departure.Id).IsSuccess.ShouldBeTrue();
        var serviceWindow = TimeWindow.Create(Now.AddHours(-11), Now.AddHours(2)).Value;
        var service = flight.AddService(new(Guid.NewGuid(), "Service"), null, serviceWindow, "Original", author, Now.AddHours(-5),
            initialPerformers: [new(receiving, serviceWindow)]).Value;
        flight.UpdateService(service.Id, service.Service, null, serviceWindow, "Latest", serviceEditor, Now.AddHours(-4)).IsSuccess.ShouldBeTrue();
        var taskWindow = TimeWindow.Create(Now.AddHours(-10), Now.AddHours(3)).Value;
        var task = flight.AddTask(FlightTaskType.Major, null, null, taskWindow, "Original", author, Now.AddHours(-3),
            initialPerformers: [new(receiving, taskWindow)]).Value;
        flight.UpdateTask(task.Id, task.TaskType, null, null, taskWindow, "Latest", taskEditor, Now.AddHours(-2)).IsSuccess.ShouldBeTrue();
        var rtr = flight.StartReturnToRamp(RtrArrivalDetails.Create(Now.AddHours(6), receiving, null).Value, rtrCreator, Now.AddMinutes(-90)).Value;
        var returnWindow = TimeWindow.Create(Now.AddHours(6), Now.AddMinutes(390)).Value;
        flight.AddService(new(Guid.NewGuid(), "RTR service"), null, returnWindow, null, rtrCreator, Now.AddMinutes(-90), rtr.Id,
            initialPerformers: [new(receiving, returnWindow)]).IsSuccess.ShouldBeTrue();
        flight.CloseReturnToRamp(rtr.Id, RtrDepartureDetails.Create(Now.AddHours(7), null, receiving, null).Value, rtrEditor, Now.AddHours(-1))
            .IsSuccess.ShouldBeTrue();
        var canceled = FlightFor(planningCreator);
        canceled.SubmitCancellation(receiving, CancellationDetails.Create(Now, "Original").Value, cancelCreator, Now.AddHours(-2)).IsSuccess.ShouldBeTrue();
        canceled.SubmitCancellation(receiving, CancellationDetails.Create(Now, "Latest").Value, cancelEditor, Now.AddMinutes(-30)).IsSuccess.ShouldBeTrue();
        var adHoc = Flight.CreateAdHoc(new(flight.Customer.CustomerId, flight.Customer.IataCode, flight.Customer.Name),
            new(flight.Station.StationId, flight.Station.IataCode, flight.Station.Name),
            new(MasterData.Contracts.Seeding.WellKnownMasterDataIds.AdHocOperationType, "AdHoc"),
            FlightNumber.CreateAdHoc("FERRY").Value, ScheduledTime.Create(flight.Schedule.Sta, flight.Schedule.Std).Value,
            adHocCreator, Now.AddHours(-1), Guid.NewGuid(), new string('A', 64)).Value;
        adHoc.UpdateAdHocDetails(adHoc.Customer, FlightNumber.CreateAdHoc("LATER").Value, adHoc.Schedule, adHocCreator, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        var planningOnly = FlightFor(planningCreator);
        db.Flights.AddRange(flight, canceled, adHoc, planningOnly); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [operations].[flight_services] SET [EndedByUserId] = {closerWithoutRecordingTime} WHERE [Id] = {service.Id}");
        var version = flight.RowVersion.ToArray(); db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();

        var rows = await db.FlightUserActivities.AsNoTracking().ToListAsync(); rows.Count.ShouldBe(9);
        void Expected(Guid user, Guid flightId, DateTimeOffset time) => rows.Single(item => item.UserId == user && item.FlightId == flightId)
            .LastInteractionAtUtc.ShouldBe(time);
        Expected(author, flight.Id, Now.AddHours(-3)); Expected(actualEditor, flight.Id, Now.AddHours(-6));
        Expected(serviceEditor, flight.Id, Now.AddHours(-4)); Expected(taskEditor, flight.Id, Now.AddHours(-2));
        Expected(rtrCreator, flight.Id, Now.AddMinutes(-90)); Expected(rtrEditor, flight.Id, Now.AddHours(-1));
        Expected(cancelCreator, canceled.Id, Now.AddHours(-2)); Expected(cancelEditor, canceled.Id, Now.AddMinutes(-30));
        Expected(adHocCreator, adHoc.Id, Now.AddHours(-1));
        rows.ShouldAllBe(item => !item.UsedSearch && item.LastInteractionAtUtc.Offset == TimeSpan.Zero);
        rows.ShouldNotContain(item => item.UserId == lostIntermediate || item.UserId == closerWithoutRecordingTime || item.UserId == planningCreator);
        rows.ShouldNotContain(item => item.FlightId == planningOnly.Id);
        (await db.Flights.AsNoTracking().Where(item => item.Id == flight.Id).Select(item => item.RowVersion).SingleAsync()).ShouldBe(version);
        (await db.Set<FlightService>().AsNoTracking().SingleAsync(item => item.Id == service.Id)).EndedByUserId.ShouldBe(closerWithoutRecordingTime);
        (await db.Flights.AsNoTracking().Where(item => item.Id == flight.Id).Select(item => item.LastReturnToRampSequence).SingleAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_activity_insert_collision_is_a_concurrency_conflict_and_rolls_back_flight_and_outbox(bool synchronous)
    {
        var database = $"ActivityRace_{Guid.NewGuid():N}";
        await using var seed = fixture.CreateContext(database); await seed.Database.MigrateAsync();
        var flight = FlightFor(Guid.NewGuid()); seed.Flights.Add(flight); await seed.SaveChangesAsync(); var originalVersion = flight.RowVersion.ToArray();
        var actor = new MobileFlightScopeContext(flight.Station.StationId, Guid.NewGuid(), Guid.NewGuid(), Now);
        await using var first = fixture.CreateContext(database); await using var loser = fixture.CreateContext(database);
        var firstFlight = await first.Flights.Include(item => item.PlannedServices).Include(item => item.AssignedEmployees).SingleAsync();
        var losingFlight = await loser.Flights.Include(item => item.PlannedServices).Include(item => item.AssignedEmployees).SingleAsync();
        await FlightActivityRecorder.TouchAsync(first, firstFlight, actor, Now.AddMinutes(2), CancellationToken.None);
        await FlightActivityRecorder.TouchAsync(loser, losingFlight, actor, Now.AddMinutes(1), CancellationToken.None);
        await first.SaveChangesAsync();
        losingFlight.ChangeFlightNumber(FlightNumber.Create("999", "SV").Value, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        loser.OutboxMessages.Add(OutboxMessage.Create(Assignment(losingFlight, actor.StaffMemberId)));

        var rejected = synchronous ? Should.Throw<DbUpdateConcurrencyException>(() => loser.SaveChanges()) :
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync());

        rejected.InnerException.ShouldBeOfType<DbUpdateException>().InnerException.ShouldBeOfType<SqlException>().Number.ShouldBeOneOf(2601, 2627);
        await using var verification = fixture.CreateContext(database);
        var retained = await verification.Flights.AsNoTracking().SingleAsync(); retained.FlightNumber.ShouldBe(flight.FlightNumber);
        retained.RowVersion.ShouldBe(originalVersion); (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
        var activity = await verification.FlightUserActivities.AsNoTracking().SingleAsync();
        activity.LastInteractionAtUtc.ShouldBe(Now.AddMinutes(2)); activity.UsedSearch.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_work_rolls_back_an_existing_activity_update_and_its_outbox(bool synchronous)
    {
        var database = $"ActivityStale_{Guid.NewGuid():N}";
        await using var seed = fixture.CreateContext(database); await seed.Database.MigrateAsync();
        var flight = FlightFor(Guid.NewGuid()); seed.Flights.Add(flight);
        var actor = new MobileFlightScopeContext(flight.Station.StationId, Guid.NewGuid(), Guid.NewGuid(), Now);
        seed.FlightUserActivities.Add(FlightUserActivity.Create(actor.UserId, flight.Id, Now)); await seed.SaveChangesAsync();
        await using var stale = fixture.CreateContext(database); var losing = await stale.Flights.SingleAsync();
        await FlightActivityRecorder.TouchAsync(stale, losing, actor, Now.AddMinutes(1), CancellationToken.None);
        await using (var winner = fixture.CreateContext(database))
        {
            var current = await winner.Flights.SingleAsync(); current.ChangeFlightNumber(FlightNumber.Create("222", "SV").Value, Now).IsSuccess.ShouldBeTrue();
            await winner.SaveChangesAsync();
        }
        losing.ChangeFlightNumber(FlightNumber.Create("333", "SV").Value, Now).IsSuccess.ShouldBeTrue();
        stale.OutboxMessages.Add(OutboxMessage.Create(Assignment(losing, actor.StaffMemberId)));
        if (synchronous) Should.Throw<DbUpdateConcurrencyException>(() => stale.SaveChanges());
        else await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verification = fixture.CreateContext(database);
        (await verification.FlightUserActivities.AsNoTracking().SingleAsync()).LastInteractionAtUtc.ShouldBe(Now);
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
        (await verification.Flights.AsNoTracking().SingleAsync()).FlightNumber.Value.ShouldBe("SV222");
    }

    [Fact]
    public async Task Unrelated_unique_constraint_failures_are_not_translated_into_activity_concurrency_conflicts()
    {
        await using var db = fixture.CreateContext(); await db.Database.MigrateAsync();
        var original = FlightFor(Guid.NewGuid()); var other = FlightFor(Guid.NewGuid());
        db.Flights.AddRange(original, other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Flights.Add(FlightFor(Guid.NewGuid(), id: original.Id));
        db.FlightUserActivities.Add(FlightUserActivity.Create(Guid.NewGuid(), other.Id, Now));
        var rejected = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        rejected.ShouldNotBeOfType<DbUpdateConcurrencyException>();
        rejected.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBeOneOf(2601, 2627);
        db.ChangeTracker.Clear(); (await db.FlightUserActivities.CountAsync()).ShouldBe(0);
    }

    private static Flight FlightFor(Guid creator, IReadOnlyList<StaffMemberSnapshot>? assigned = null, Guid? id = null) =>
        Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Customer"), new(Guid.NewGuid(), "RUH", "Station"), new(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("100", "SV").Value, ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value,
            null, [new(Guid.NewGuid(), "Service")], assigned ?? [], creator, Now.AddHours(-11), id).Value;
    private static StaffMemberSnapshot Employee() => new(Guid.NewGuid(), "Employee", "E1");
    private static FlightEmployeeAssigned Assignment(Flight flight, Guid staffId) => new()
        { FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, StaffMemberId = staffId, OccurredOnUtc = Now };

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        public OperationsDbContext CreateContext(string? database = null)
        {
            var connection = new SqlConnectionStringBuilder(sql.GetConnectionString()) { InitialCatalog = database ?? $"Activity_{Guid.NewGuid():N}" };
            return new(new DbContextOptionsBuilder<OperationsDbContext>().UseSqlServer(connection.ConnectionString,
                settings => settings.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema)).Options);
        }
        public async Task InitializeAsync() => await sql.StartAsync();
        public async Task DisposeAsync() => await sql.DisposeAsync();
    }
}
