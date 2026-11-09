using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightDeparturePersistenceTests(FlightDeparturePersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightDeparturePersistenceTests.DatabaseFixture>
{
    private const string ArrivalBaseline = "20261107224214_Operations_FlightArrivalSubmissions";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Departure_migration_preserves_flight_and_arrival_data_and_starts_with_no_departures()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(ArrivalBaseline);
        var existingMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var employee = Employee("Assigned receiving employee", "E100");
        var flight = ScheduleFlight(employee);
        var arrivalDetails = Arrival(flight.AircraftType!, employee);
        var arrivalUserId = Guid.NewGuid();
        var arrival = flight.SubmitArrival(employee, arrivalDetails, arrivalUserId, Now.AddMinutes(1)).Value;
        await PreRtrMigrationSeed.SaveAsync(db, flight);
        var rowVersion = flight.RowVersion.ToArray();
        var plannedServiceId = flight.PlannedServices.Single().Id;
        var assignmentId = flight.AssignedEmployees.Single().Id;
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync("20261108183853_Operations_FlightDepartureSubmissions");

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(existingMigrations.Length + 1);
        appliedMigrations.Take(existingMigrations.Length).ShouldBe(existingMigrations);
        // Load with the current model after verifying the historical migration boundary.
        await db.Database.MigrateAsync();
        var reloaded = await db.Flights.AsNoTracking()
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .SingleAsync();
        reloaded.Id.ShouldBe(flight.Id);
        reloaded.Customer.ShouldBe(flight.Customer);
        reloaded.Station.ShouldBe(flight.Station);
        reloaded.OperationType.ShouldBe(flight.OperationType);
        reloaded.FlightNumber.ShouldBe(flight.FlightNumber);
        reloaded.Schedule.ShouldBe(flight.Schedule);
        reloaded.AircraftType.ShouldBe(flight.AircraftType);
        reloaded.CreatedByUserId.ShouldBe(flight.CreatedByUserId);
        reloaded.CreatedAtUtc.ShouldBe(flight.CreatedAtUtc);
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        reloaded.RowVersion.ShouldBe(rowVersion);
        reloaded.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(plannedServiceId);
        var assignment = reloaded.AssignedEmployees.ShouldHaveSingleItem();
        assignment.Id.ShouldBe(assignmentId);
        assignment.Employee.ShouldBe(employee);
        var preservedArrival = reloaded.ArrivalSubmissions.ShouldHaveSingleItem();
        preservedArrival.Id.ShouldBe(arrival.Id);
        preservedArrival.SubmittedByEmployee.ShouldBe(employee);
        preservedArrival.Details.ShouldBe(arrivalDetails);
        preservedArrival.IsAccepted.ShouldBeTrue();
        preservedArrival.CreatedByUserId.ShouldBe(arrivalUserId);
        preservedArrival.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        preservedArrival.UpdatedByUserId.ShouldBeNull();
        preservedArrival.UpdatedAtUtc.ShouldBeNull();
        reloaded.DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Departures_round_trip_signature_metadata_and_update_without_affecting_arrival_acceptance()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var submitter = Employee("Submitting employee", "E101");
        var handler = Employee("Departure handling employee", "E102");
        flight.SubmitArrival(submitter, Arrival(flight.AircraftType!, submitter), Guid.NewGuid(), Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        var createdBy = Guid.NewGuid();
        var signature = CustomerSignature.Create("customer/departure-original.png", "signatures/departure-original.png", "image/png", 1024, Now.AddHours(2).ToOffset(TimeSpan.FromHours(3))).Value;
        var initialDetails = DepartureDetails.Create(Now.AddHours(2).ToOffset(TimeSpan.FromHours(3)),
            signature, handler, null).Value;
        var first = flight.SubmitDeparture(submitter, initialDetails, createdBy, Now.AddHours(2)).Value;
        var submissionId = first.Id;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(submissionId);
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.SubmittedByEmployee.ShouldBe(submitter);
        persisted.Details.ShouldBe(initialDetails);
        persisted.Details.Atd.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Details.CustomerSignature.FileName.ShouldBe("departure-original.png");
        persisted.Details.CustomerSignature.StorageKey.ShouldBe(signature.StorageKey);
        persisted.Details.CustomerSignature.ContentType.ShouldBe(signature.ContentType);
        persisted.Details.CustomerSignature.SizeBytes.ShouldBe(signature.SizeBytes);
        persisted.Details.CustomerSignature.SignedAtUtc.ShouldBe(signature.SignedAtUtc);
        persisted.Details.CustomerSignature.SignedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Details.HandledByEmployee.ShouldBe(handler);
        persisted.Details.Remarks.ShouldBeNull();
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddHours(2));
        persisted.UpdatedByUserId.ShouldBeNull();
        persisted.UpdatedAtUtc.ShouldBeNull();
        var versionBeforeUpdate = reloaded.RowVersion.ToArray();

        var updatedBy = Guid.NewGuid();
        var editedDetails = Departure("Updated departure remarks", handler, "signatures/departure-updated.png");
        reloaded.SubmitDeparture(submitter, editedDetails, updatedBy, Now.AddHours(2).AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(submissionId);
        persisted.Details.ShouldBe(editedDetails);
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddHours(2));
        persisted.UpdatedByUserId.ShouldBe(updatedBy);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddHours(2).AddMinutes(1));
        reloaded.RowVersion.SequenceEqual(versionBeforeUpdate).ShouldBeFalse();

        var competingSubmitter = Employee("Second submitting employee", "E103");
        var competingDetails = Departure("Independent departure input", handler, "signatures/second-employee.png");
        reloaded.SubmitDeparture(competingSubmitter, competingDetails, Guid.NewGuid(), Now.AddHours(2).AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await db.Flights
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .SingleAsync(item => item.Id == flight.Id);
        reloaded.DepartureSubmissions.Count.ShouldBe(2);
        reloaded.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.DepartureSubmissions.Single(item => item.Id == submissionId).Details.ShouldBe(editedDetails);
        reloaded.DepartureSubmissions.Single(item => item.SubmittedByEmployee.StaffMemberId == competingSubmitter.StaffMemberId)
            .Details.ShouldBe(competingDetails);
        reloaded.ArrivalSubmissions.ShouldHaveSingleItem().IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public async Task Reused_signature_details_and_staff_instances_persist_as_independent_departure_values()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var handlerAndSubmitter = Employee("Assigned departure handling employee", "E150");
        var secondSubmitter = Employee("Second submitting employee", "E151");
        var flight = ScheduleFlight(handlerAndSubmitter);
        var sharedSignature = CustomerSignature.Create("shared.png", "signatures/shared.png", "image/png", 2048, Now.AddHours(2)).Value;
        var sharedDetails = DepartureDetails.Create(Now.AddHours(2), sharedSignature, handlerAndSubmitter,
            "Both employees recorded these departure details").Value;
        var first = flight.SubmitDeparture(handlerAndSubmitter, sharedDetails, Guid.NewGuid(), Now.AddHours(2)).Value;
        var second = flight.SubmitDeparture(secondSubmitter, sharedDetails, Guid.NewGuid(), Now.AddHours(2).AddMinutes(1)).Value;
        first.Details.CustomerSignature.ShouldNotBeSameAs(sharedSignature);
        second.Details.CustomerSignature.ShouldNotBeSameAs(sharedSignature);
        first.Details.CustomerSignature.ShouldNotBeSameAs(second.Details.CustomerSignature);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.Flights
            .Include(item => item.AssignedEmployees)
            .Include(item => item.DepartureSubmissions)
            .SingleAsync(item => item.Id == flight.Id);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(handlerAndSubmitter);
        reloaded.DepartureSubmissions.Count.ShouldBe(2);
        reloaded.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        var firstPersisted = reloaded.DepartureSubmissions.Single(item => item.Id == first.Id);
        var secondPersisted = reloaded.DepartureSubmissions.Single(item => item.Id == second.Id);
        firstPersisted.SubmittedByEmployee.ShouldBe(handlerAndSubmitter);
        secondPersisted.SubmittedByEmployee.ShouldBe(secondSubmitter);
        firstPersisted.Details.ShouldBe(sharedDetails);
        secondPersisted.Details.ShouldBe(sharedDetails);
        firstPersisted.Details.CustomerSignature.ShouldBe(sharedSignature);
        secondPersisted.Details.CustomerSignature.ShouldBe(sharedSignature);
        firstPersisted.Details.CustomerSignature.ShouldNotBeSameAs(secondPersisted.Details.CustomerSignature);
        firstPersisted.Details.HandledByEmployee.ShouldBe(handlerAndSubmitter);
        secondPersisted.Details.HandledByEmployee.ShouldBe(handlerAndSubmitter);
    }

    [Fact]
    public async Task Database_limits_submitter_and_accepted_departure_uniqueness_to_each_flight()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var firstEmployee = Employee("First employee", "E201");
        var first = flight.SubmitDeparture(firstEmployee, Departure(), Guid.NewGuid(), Now.AddHours(2)).Value;
        var second = flight.SubmitDeparture(Employee("Second employee", "E202"), Departure(), Guid.NewGuid(), Now.AddHours(2).AddMinutes(1)).Value;
        var otherFlight = ScheduleFlight();
        otherFlight.SubmitDeparture(firstEmployee, Departure(), Guid.NewGuid(), Now.AddHours(2))
            .IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_departure_submissions]
            SET [IsAccepted] = 1 WHERE [Id] = {first.Id}
            """);
        var acceptanceError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_departure_submissions]
                SET [IsAccepted] = 1 WHERE [Id] = {second.Id}
                """));
        acceptanceError.Number.ShouldBeOneOf(2601, 2627);

        var submitterError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_departure_submissions]
                SET [SubmittedByStaffMemberId] = {firstEmployee.StaffMemberId} WHERE [Id] = {second.Id}
                """));
        submitterError.Number.ShouldBeOneOf(2601, 2627);
        db.ChangeTracker.Clear();
        var persistedOtherFlight = await LoadFlightAsync(db, otherFlight.Id);
        var otherDeparture = persistedOtherFlight.DepartureSubmissions.ShouldHaveSingleItem();
        otherDeparture.SubmittedByEmployee.ShouldBe(firstEmployee);
        otherDeparture.IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public async Task Stale_aggregate_rejects_departure_write_and_explicit_reload_preserves_all_inputs()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        flight.SubmitDeparture(Employee("Initial employee", "E301"), Departure(), Guid.NewGuid(), Now.AddHours(2))
            .IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var firstFlight = await LoadFlightAsync(db, flight.Id);
        var staleFlight = await LoadFlightAsync(competingDb, flight.Id);
        var thirdEmployee = Employee("Third employee", "E303");
        var thirdDetails = Departure("Entered from stale flight", thirdEmployee, "signatures/third-employee.png");
        var thirdUserId = Guid.NewGuid();
        firstFlight.SubmitDeparture(Employee("Second employee", "E302"), Departure(), Guid.NewGuid(), Now.AddHours(2).AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        staleFlight.SubmitDeparture(thirdEmployee, thirdDetails, thirdUserId, Now.AddHours(2).AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => competingDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.DepartureSubmissions.Count.ShouldBe(2);
        currentFlight.SubmitDeparture(thirdEmployee, thirdDetails, thirdUserId, Now.AddHours(2).AddMinutes(3))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.DepartureSubmissions.Count.ShouldBe(3);
        currentFlight.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        currentFlight.DepartureSubmissions.Single(item => item.SubmittedByEmployee.StaffMemberId == thirdEmployee.StaffMemberId)
            .Details.ShouldBe(thirdDetails);
    }

    private static Task<Flight> LoadFlightAsync(OperationsDbContext db, Guid flightId) =>
        db.Flights.Include(item => item.DepartureSubmissions).SingleAsync(item => item.Id == flightId);

    private static Flight ScheduleFlight(StaffMemberSnapshot? assignedEmployee = null) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020", "SV").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployee is null ? [] : [assignedEmployee],
            Guid.NewGuid(), Now).Value;

    private static StaffMemberSnapshot Employee(string name, string employeeId) => new(Guid.NewGuid(), name, employeeId);

    private static ArrivalDetails Arrival(AircraftTypeSnapshot aircraft, StaffMemberSnapshot receiver) =>
        ArrivalDetails.Create("ARRIVAL-REFERENCE", Now.ToOffset(TimeSpan.FromHours(3)), aircraft,
            "HZ-123", receiver, "Arrival recorded before departure model migration").Value;

    private static DepartureDetails Departure(string? remarks = null, StaffMemberSnapshot? handler = null,
        string storageKey = "signatures/departure.png") =>
        DepartureDetails.Create(Now.AddHours(2).ToOffset(TimeSpan.FromHours(3)),
            CustomerSignature.Create(Path.GetFileName(storageKey), storageKey, "image/png", 1024, Now.AddHours(2)).Value,
            handler ?? Employee("Departure handling employee", "E400"), remarks).Value;

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightDeparture_{Guid.NewGuid():N}"
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
