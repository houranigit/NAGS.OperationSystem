using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightArrivalPersistenceTests(FlightArrivalPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightArrivalPersistenceTests.DatabaseFixture>
{
    private const string SchedulingBaseline = "20261103214037_Operations_FlightSchedulingBaseline";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Arrival_migration_preserves_existing_planning_and_starts_with_no_submissions()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(SchedulingBaseline);
        var existingMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var plannedAircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var assignedEmployee = Employee("Assigned employee", "E100");
        var flight = ScheduleFlight(plannedAircraft, assignedEmployee);
        await PreRtrMigrationSeed.SaveAsync(db, flight);
        var rowVersion = flight.RowVersion.ToArray();
        var plannedServiceId = flight.PlannedServices.Single().Id;
        var assignmentId = flight.AssignedEmployees.Single().Id;
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync("20261107224214_Operations_FlightArrivalSubmissions");

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(existingMigrations.Length + 1);
        appliedMigrations.Take(existingMigrations.Length).ShouldBe(existingMigrations);
        // Load with the current model after verifying the historical migration boundary.
        await db.Database.MigrateAsync();
        var reloaded = await db.Flights.AsNoTracking()
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .SingleAsync();
        reloaded.Id.ShouldBe(flight.Id);
        reloaded.Customer.ShouldBe(flight.Customer);
        reloaded.Station.ShouldBe(flight.Station);
        reloaded.OperationType.ShouldBe(flight.OperationType);
        reloaded.FlightNumber.ShouldBe(flight.FlightNumber);
        reloaded.Schedule.ShouldBe(flight.Schedule);
        reloaded.AircraftType.ShouldBe(plannedAircraft);
        reloaded.CreatedByUserId.ShouldBe(flight.CreatedByUserId);
        reloaded.CreatedAtUtc.ShouldBe(flight.CreatedAtUtc);
        reloaded.UpdatedAtUtc.ShouldBeNull();
        reloaded.RowVersion.ShouldBe(rowVersion);
        reloaded.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(plannedServiceId);
        var assignment = reloaded.AssignedEmployees.ShouldHaveSingleItem();
        assignment.Id.ShouldBe(assignmentId);
        assignment.Employee.ShouldBe(assignedEmployee);
        reloaded.ArrivalSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Submissions_round_trip_update_in_place_and_keep_competing_employee_input()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var plannedAircraft = flight.AircraftType;
        var submitter = Employee("Submitting employee", "E101");
        var receiver = Employee("Receiving employee", "E102");
        var createdBy = Guid.NewGuid();
        var initialDetails = ArrivalDetails.Create(null, Now.ToOffset(TimeSpan.FromHours(3)),
            plannedAircraft, "HZ-123", receiver, null).Value;
        var first = flight.SubmitArrival(submitter, initialDetails, createdBy, Now.AddMinutes(1)).Value;
        var submissionId = first.Id;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.ArrivalSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(submissionId);
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.SubmittedByEmployee.ShouldBe(submitter);
        persisted.Details.ShouldBe(initialDetails);
        persisted.Details.CustomerWorkReferenceNumber.ShouldBeNull();
        persisted.Details.Remarks.ShouldBeNull();
        persisted.Details.Ata.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Details.ReceivedByEmployee.ShouldBe(receiver);
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBeNull();
        persisted.UpdatedAtUtc.ShouldBeNull();
        reloaded.AircraftType.ShouldBe(plannedAircraft);
        var versionBeforeUpdate = reloaded.RowVersion.ToArray();

        var updatedBy = Guid.NewGuid();
        var editedDetails = Arrival("CUSTOMER-42", "Updated arrival remarks", receiver);
        reloaded.SubmitArrival(submitter, editedDetails, updatedBy, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.ArrivalSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(submissionId);
        persisted.Details.ShouldBe(editedDetails);
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBe(updatedBy);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(2));
        reloaded.RowVersion.SequenceEqual(versionBeforeUpdate).ShouldBeFalse();

        var competingSubmitter = Employee("Second submitting employee", "E103");
        var competingDetails = Arrival("OTHER-REFERENCE", "Independent input", receiver);
        reloaded.SubmitArrival(competingSubmitter, competingDetails, Guid.NewGuid(), Now.AddMinutes(3))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.ArrivalSubmissions.Count.ShouldBe(2);
        reloaded.ArrivalSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.ArrivalSubmissions.Single(item => item.Id == submissionId).Details.ShouldBe(editedDetails);
        reloaded.ArrivalSubmissions.Single(item => item.SubmittedByEmployee.StaffMemberId == competingSubmitter.StaffMemberId)
            .Details.ShouldBe(competingDetails);
        reloaded.AircraftType.ShouldBe(plannedAircraft);
    }

    [Fact]
    public async Task Reused_detail_and_snapshot_instances_persist_as_independent_submission_values()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var receiverAndSubmitter = Employee("Assigned receiving employee", "E150");
        var secondSubmitter = Employee("Second submitting employee", "E151");
        var flight = ScheduleFlight(aircraft, receiverAndSubmitter);
        var sharedDetails = ArrivalDetails.Create("SHARED-REFERENCE", Now, aircraft, "HZ-150",
            receiverAndSubmitter, "Both employees recorded these arrival details").Value;
        var first = flight.SubmitArrival(receiverAndSubmitter, sharedDetails, Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var second = flight.SubmitArrival(secondSubmitter, sharedDetails, Guid.NewGuid(), Now.AddMinutes(2)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.Flights
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .SingleAsync(item => item.Id == flight.Id);
        reloaded.AircraftType.ShouldBe(aircraft);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(receiverAndSubmitter);
        reloaded.ArrivalSubmissions.Count.ShouldBe(2);
        reloaded.ArrivalSubmissions.ShouldAllBe(item => !item.IsAccepted);
        var firstPersisted = reloaded.ArrivalSubmissions.Single(item => item.Id == first.Id);
        var secondPersisted = reloaded.ArrivalSubmissions.Single(item => item.Id == second.Id);
        firstPersisted.SubmittedByEmployee.ShouldBe(receiverAndSubmitter);
        secondPersisted.SubmittedByEmployee.ShouldBe(secondSubmitter);
        firstPersisted.Details.ShouldBe(sharedDetails);
        secondPersisted.Details.ShouldBe(sharedDetails);
        firstPersisted.Details.ReceivedByEmployee.ShouldBe(receiverAndSubmitter);
        secondPersisted.Details.ReceivedByEmployee.ShouldBe(receiverAndSubmitter);
    }

    [Fact]
    public async Task Database_rejects_duplicate_submitters_and_multiple_accepted_submissions()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var firstEmployee = Employee("First employee", "E201");
        var first = flight.SubmitArrival(firstEmployee, Arrival(), Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var second = flight.SubmitArrival(Employee("Second employee", "E202"), Arrival(), Guid.NewGuid(), Now.AddMinutes(2)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_arrival_submissions]
            SET [IsAccepted] = 1 WHERE [Id] = {first.Id}
            """);
        var acceptanceError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_arrival_submissions]
                SET [IsAccepted] = 1 WHERE [Id] = {second.Id}
                """));
        acceptanceError.Number.ShouldBeOneOf(2601, 2627);

        var submitterError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_arrival_submissions]
                SET [SubmittedByStaffMemberId] = {firstEmployee.StaffMemberId} WHERE [Id] = {second.Id}
                """));
        submitterError.Number.ShouldBeOneOf(2601, 2627);
    }

    [Fact]
    public async Task Stale_aggregate_cannot_overwrite_arrivals_and_can_be_explicitly_reloaded_and_resubmitted()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        flight.SubmitArrival(Employee("Initial employee", "E301"), Arrival(), Guid.NewGuid(), Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var firstFlight = await LoadFlightAsync(db, flight.Id);
        var staleFlight = await LoadFlightAsync(competingDb, flight.Id);
        var thirdEmployee = Employee("Third employee", "E303");
        var thirdDetails = Arrival("THIRD", "Entered from stale flight", thirdEmployee);
        var thirdUserId = Guid.NewGuid();
        firstFlight.SubmitArrival(Employee("Second employee", "E302"), Arrival(), Guid.NewGuid(), Now.AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        staleFlight.SubmitArrival(thirdEmployee, thirdDetails, thirdUserId, Now.AddMinutes(3))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => competingDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.ArrivalSubmissions.Count.ShouldBe(2);
        currentFlight.SubmitArrival(thirdEmployee, thirdDetails, thirdUserId, Now.AddMinutes(4))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.ArrivalSubmissions.Count.ShouldBe(3);
        currentFlight.ArrivalSubmissions.ShouldAllBe(item => !item.IsAccepted);
        currentFlight.ArrivalSubmissions.Single(item => item.SubmittedByEmployee.StaffMemberId == thirdEmployee.StaffMemberId)
            .Details.ShouldBe(thirdDetails);
    }

    private static Task<Flight> LoadFlightAsync(OperationsDbContext db, Guid flightId) =>
        db.Flights.Include(item => item.ArrivalSubmissions).SingleAsync(item => item.Id == flightId);

    private static Flight ScheduleFlight(AircraftTypeSnapshot? plannedAircraft = null, StaffMemberSnapshot? assignedEmployee = null) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020", "SV").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            plannedAircraft ?? new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployee is null ? [] : [assignedEmployee],
            Guid.NewGuid(), Now).Value;

    private static StaffMemberSnapshot Employee(string name, string employeeId) => new(Guid.NewGuid(), name, employeeId);

    private static ArrivalDetails Arrival(string? reference = null, string? remarks = null, StaffMemberSnapshot? receiver = null) =>
        ArrivalDetails.Create(reference, Now.ToOffset(TimeSpan.FromHours(3)),
            new AircraftTypeSnapshot(Guid.NewGuid(), "Boeing", "737"), "HZ-123",
            receiver ?? Employee("Receiving employee", "E400"), remarks).Value;

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightArrival_{Guid.NewGuid():N}"
            };
            var options = new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer(connection.ConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema))
                .Options;
            return new OperationsDbContext(options);
        }

        public async Task InitializeAsync() => await _sql.StartAsync();

        public async Task DisposeAsync() => await _sql.DisposeAsync();
    }
}
