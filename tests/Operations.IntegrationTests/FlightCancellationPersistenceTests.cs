using BuildingBlocks.Domain.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightCancellationPersistenceTests(FlightCancellationPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightCancellationPersistenceTests.DatabaseFixture>
{
    private const string DepartureBaseline = "20261108183853_Operations_FlightDepartureSubmissions";
    private const string CancellationMigration = "20261108190121_Operations_FlightCancellationSubmissions";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cancellation_migration_preserves_existing_flight_and_operational_data()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(DepartureBaseline);
        var previousMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var employee = Employee("Assigned engineer", "E100");
        var flight = ScheduleFlight(employee);
        SubmitOperation(flight, true, employee, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        SubmitOperation(flight, false, employee, Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        await PreRtrMigrationSeed.SaveAsync(db, flight);
        var rowVersion = flight.RowVersion.ToArray();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync(CancellationMigration);

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(previousMigrations.Length + 1);
        appliedMigrations.Take(previousMigrations.Length).ShouldBe(previousMigrations);
        appliedMigrations.Last().ShouldBe(CancellationMigration);
        // Load with the current model after verifying the historical migration boundary.
        await db.Database.MigrateAsync();
        var reloaded = await db.Flights.AsNoTracking()
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .Include(item => item.CancellationSubmissions)
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
        reloaded.UpdatedAtUtc.ShouldBe(flight.UpdatedAtUtc);
        reloaded.RowVersion.ShouldBe(rowVersion);
        reloaded.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(flight.PlannedServices.Single().Id);
        var assignment = reloaded.AssignedEmployees.ShouldHaveSingleItem();
        assignment.Id.ShouldBe(flight.AssignedEmployees.Single().Id);
        assignment.Employee.ShouldBe(employee);
        var arrival = reloaded.ArrivalSubmissions.ShouldHaveSingleItem();
        arrival.Id.ShouldBe(flight.ArrivalSubmissions.Single().Id);
        arrival.Details.ShouldBe(flight.ArrivalSubmissions.Single().Details);
        arrival.CreatedByUserId.ShouldBe(flight.ArrivalSubmissions.Single().CreatedByUserId);
        arrival.CreatedAtUtc.ShouldBe(flight.ArrivalSubmissions.Single().CreatedAtUtc);
        arrival.IsAccepted.ShouldBeTrue();
        var departure = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        departure.Id.ShouldBe(flight.DepartureSubmissions.Single().Id);
        departure.Details.ShouldBe(flight.DepartureSubmissions.Single().Details);
        departure.CreatedByUserId.ShouldBe(flight.DepartureSubmissions.Single().CreatedByUserId);
        departure.CreatedAtUtc.ShouldBe(flight.DepartureSubmissions.Single().CreatedAtUtc);
        departure.IsAccepted.ShouldBeTrue();
        reloaded.CancellationSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_submissions_round_trip_optional_details_and_update_only_the_authors_record()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var firstEmployee = Employee("First submitting engineer", "E101");
        var createdBy = Guid.NewGuid();
        var initialDetails = CancellationDetails.Create(Now.ToOffset(TimeSpan.FromHours(3)), "  ").Value;
        var first = flight.SubmitCancellation(firstEmployee, initialDetails, createdBy,
            Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-6))).Value;
        var firstId = first.Id;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.CancellationSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(firstId);
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.SubmittedByEmployee.ShouldBe(firstEmployee);
        persisted.Details.ShouldBe(initialDetails);
        persisted.Details.CanceledAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Details.Remarks.ShouldBeNull();
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBeNull();
        persisted.UpdatedAtUtc.ShouldBeNull();
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        var firstVersion = reloaded.RowVersion.ToArray();

        var updatedBy = Guid.NewGuid();
        var editedDetails = CancellationDetails.Create(Now.AddMinutes(2), "  Corrected customer remarks  ").Value;
        reloaded.SubmitCancellation(firstEmployee, editedDetails, updatedBy, Now.AddMinutes(3))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.CancellationSubmissions.ShouldHaveSingleItem();
        persisted.Id.ShouldBe(firstId);
        persisted.SubmittedByEmployee.ShouldBe(firstEmployee);
        persisted.Details.ShouldBe(editedDetails);
        persisted.Details.Remarks.ShouldBe("Corrected customer remarks");
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBe(updatedBy);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        reloaded.RowVersion.SequenceEqual(firstVersion).ShouldBeFalse();
        var editedVersion = reloaded.RowVersion.ToArray();
        var equivalent = CancellationDetails.Create(editedDetails.CanceledAtUtc.ToOffset(TimeSpan.FromHours(3)),
            "Corrected customer remarks").Value;
        reloaded.SubmitCancellation(firstEmployee, equivalent, Guid.NewGuid(), Now.AddMinutes(4))
            .IsSuccess.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        reloaded.RowVersion.ShouldBe(editedVersion);

        var secondEmployee = Employee("Second submitting engineer", "E102");
        var secondDetails = CancellationDetails.Create(Now.AddMinutes(4), "Independent cancellation input").Value;
        var secondId = reloaded.SubmitCancellation(secondEmployee, secondDetails, Guid.NewGuid(), Now.AddMinutes(4)).Value.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.CancellationSubmissions.Count.ShouldBe(2);
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.CancellationSubmissions.Single(item => item.Id == firstId).Details.ShouldBe(editedDetails);
        reloaded.CancellationSubmissions.Single(item => item.Id == secondId).Details.ShouldBe(secondDetails);

        var thirdEmployee = Employee("Third submitting engineer", "E103");
        var thirdDetails = CancellationDetails.Create(Now.AddMinutes(5), null).Value;
        var thirdId = reloaded.SubmitCancellation(thirdEmployee, thirdDetails, Guid.NewGuid(), Now.AddMinutes(5)).Value.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.CancellationSubmissions.Count.ShouldBe(3);
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.CancellationSubmissions.Single(item => item.Id == firstId).Details.ShouldBe(editedDetails);
        reloaded.CancellationSubmissions.Single(item => item.Id == secondId).Details.ShouldBe(secondDetails);
        reloaded.CancellationSubmissions.Single(item => item.Id == thirdId).Details.ShouldBe(thirdDetails);
        reloaded.ArrivalSubmissions.ShouldBeEmpty();
        reloaded.DepartureSubmissions.ShouldBeEmpty();
        reloaded.Schedule.ShouldBe(flight.Schedule);
    }

    [Fact]
    public async Task Reused_cancellation_details_and_staff_instances_persist_as_independent_submission_values()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var assignedSubmitter = Employee("Assigned submitting engineer", "E150");
        var secondSubmitter = Employee("Second submitting engineer", "E151");
        var flight = ScheduleFlight(assignedSubmitter);
        var sharedDetails = CancellationDetails.Create(Now, "Both employees recorded this cancellation").Value;
        var first = flight.SubmitCancellation(assignedSubmitter, sharedDetails, Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var second = flight.SubmitCancellation(secondSubmitter, sharedDetails, Guid.NewGuid(), Now.AddMinutes(2)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.Flights
            .Include(item => item.AssignedEmployees)
            .Include(item => item.CancellationSubmissions)
            .SingleAsync(item => item.Id == flight.Id);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(assignedSubmitter);
        reloaded.CancellationSubmissions.Count.ShouldBe(2);
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        var firstPersisted = reloaded.CancellationSubmissions.Single(item => item.Id == first.Id);
        var secondPersisted = reloaded.CancellationSubmissions.Single(item => item.Id == second.Id);
        firstPersisted.SubmittedByEmployee.ShouldBe(assignedSubmitter);
        secondPersisted.SubmittedByEmployee.ShouldBe(secondSubmitter);
        firstPersisted.Details.ShouldBe(sharedDetails);
        secondPersisted.Details.ShouldBe(sharedDetails);
    }

    [Fact]
    public async Task Database_limits_submitter_and_accepted_cancellation_uniqueness_to_each_flight()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var firstEmployee = Employee("First engineer", "E201");
        var details = CancellationDetails.Create(Now, null).Value;
        var first = flight.SubmitCancellation(firstEmployee, details, Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var second = flight.SubmitCancellation(Employee("Second engineer", "E202"), details,
            Guid.NewGuid(), Now.AddMinutes(2)).Value;
        var otherFlight = ScheduleFlight();
        otherFlight.SubmitCancellation(firstEmployee, details, Guid.NewGuid(), Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_cancellation_submissions]
            SET [IsAccepted] = 1 WHERE [Id] = {first.Id}
            """);
        var acceptanceError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_cancellation_submissions]
                SET [IsAccepted] = 1 WHERE [Id] = {second.Id}
                """));
        acceptanceError.Number.ShouldBeOneOf(2601, 2627);

        var submitterError = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_cancellation_submissions]
                SET [SubmittedByStaffMemberId] = {firstEmployee.StaffMemberId} WHERE [Id] = {second.Id}
                """));
        submitterError.Number.ShouldBeOneOf(2601, 2627);
        db.ChangeTracker.Clear();
        var persistedOtherFlight = await LoadFlightAsync(db, otherFlight.Id);
        var otherCancellation = persistedOtherFlight.CancellationSubmissions.ShouldHaveSingleItem();
        otherCancellation.SubmittedByEmployee.ShouldBe(firstEmployee);
        otherCancellation.IsAccepted.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Own_pending_submissions_exclude_the_opposite_branch_after_reload(bool cancellationFirst, bool isArrival)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var firstEmployee = Employee("First engineer", "E301");
        var secondEmployee = Employee("Second engineer", "E302");
        var details = CancellationDetails.Create(Now, "Pending cancellation review").Value;
        if (cancellationFirst)
        {
            flight.SubmitCancellation(firstEmployee, details, Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
            flight.SubmitCancellation(secondEmployee, details, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        }
        else
        {
            SubmitOperation(flight, isArrival, firstEmployee, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
            SubmitOperation(flight, isArrival, secondEmployee, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        }
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var rowVersion = reloaded.RowVersion.ToArray();
        if (cancellationFirst)
        {
            SubmitOperation(reloaded, isArrival, firstEmployee, Now.AddMinutes(3)).Error.Code
                .ShouldBe("Operations.Flight.CancellationSubmitted");
        }
        else
        {
            reloaded.SubmitCancellation(firstEmployee, details, Guid.NewGuid(), Now.AddMinutes(3)).Error.Code
                .ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        }
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(2));
        (await db.SaveChangesAsync()).ShouldBe(0);
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(rowVersion);
        reloaded.CancellationSubmissions.Count.ShouldBe(cancellationFirst ? 2 : 0);
        reloaded.ArrivalSubmissions.Count.ShouldBe(!cancellationFirst && isArrival ? 2 : 0);
        reloaded.DepartureSubmissions.Count.ShouldBe(!cancellationFirst && !isArrival ? 2 : 0);
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.ArrivalSubmissions.ShouldAllBe(item => !item.IsAccepted);
        reloaded.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
    }

    [Fact]
    public async Task Stale_cancellation_add_can_be_explicitly_reloaded_and_resubmitted_without_losing_either_input()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var matchingClock = Now.AddMinutes(10);
        flight.UpdatePlanning(flight.Schedule, flight.AircraftType,
            flight.PlannedServices.Select(item => item.Service).ToArray(), null, matchingClock)
            .IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var firstFlight = await LoadFlightAsync(db, flight.Id);
        var staleFlight = await LoadFlightAsync(competingDb, flight.Id);
        var firstEmployee = Employee("First submitting engineer", "E350");
        var secondEmployee = Employee("Second submitting engineer", "E351");
        var firstDetails = CancellationDetails.Create(Now, "First employee's cancellation input").Value;
        var secondDetails = CancellationDetails.Create(Now.AddMinutes(1), "Second employee's independent input").Value;
        var firstId = firstFlight.SubmitCancellation(firstEmployee, firstDetails, Guid.NewGuid(), matchingClock).Value.Id;
        var secondUserId = Guid.NewGuid();
        staleFlight.SubmitCancellation(secondEmployee, secondDetails, secondUserId, matchingClock)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateException>(() => competingDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var currentFlight = await LoadFlightAsync(db, flight.Id);
        var firstPersisted = currentFlight.CancellationSubmissions.ShouldHaveSingleItem();
        firstPersisted.Id.ShouldBe(firstId);
        firstPersisted.Details.ShouldBe(firstDetails);
        firstPersisted.IsAccepted.ShouldBeTrue();
        currentFlight.SubmitCancellation(secondEmployee, secondDetails, secondUserId, matchingClock)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.CancellationSubmissions.Count.ShouldBe(2);
        currentFlight.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        currentFlight.CancellationSubmissions.Single(item => item.Id == firstId).Details.ShouldBe(firstDetails);
        var secondPersisted = currentFlight.CancellationSubmissions.Single(
            item => item.SubmittedByEmployee.StaffMemberId == secondEmployee.StaffMemberId);
        secondPersisted.Details.ShouldBe(secondDetails);
        secondPersisted.CreatedByUserId.ShouldBe(secondUserId);
        currentFlight.UpdatedAtUtc.ShouldBe(matchingClock);
        currentFlight.ArrivalSubmissions.ShouldBeEmpty();
        currentFlight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    public async Task Matching_clock_cancellation_and_operation_reject_stale_write_and_reloaded_domain_conflict(
        bool isArrival, bool cancellationWins, bool synchronousSave)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var matchingClock = Now.AddMinutes(1);
        flight.UpdatePlanning(flight.Schedule, flight.AircraftType,
            flight.PlannedServices.Select(item => item.Service).ToArray(), null, matchingClock)
            .IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await SaveAsync(db, synchronousSave);
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winningFlight = await LoadFlightAsync(db, flight.Id);
        var staleFlight = await LoadFlightAsync(competingDb, flight.Id);
        winningFlight.UpdatedAtUtc.ShouldBe(matchingClock);
        staleFlight.UpdatedAtUtc.ShouldBe(matchingClock);
        var employee = Employee("Submitting engineer", "E400");
        var details = CancellationDetails.Create(Now, "Canceled by customer").Value;
        var cancellationUserId = Guid.NewGuid();
        if (cancellationWins)
        {
            winningFlight.SubmitCancellation(employee, details, cancellationUserId, matchingClock).IsSuccess.ShouldBeTrue();
            SubmitOperation(staleFlight, isArrival, employee, matchingClock).IsSuccess.ShouldBeTrue();
        }
        else
        {
            SubmitOperation(winningFlight, isArrival, employee, matchingClock).IsSuccess.ShouldBeTrue();
            staleFlight.SubmitCancellation(employee, details, cancellationUserId, matchingClock).IsSuccess.ShouldBeTrue();
        }
        await SaveAsync(db, synchronousSave);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => SaveAsync(competingDb, synchronousSave));
        db.ChangeTracker.Clear();
        var currentFlight = await LoadFlightAsync(db, flight.Id);
        var winnerVersion = currentFlight.RowVersion.ToArray();
        if (cancellationWins)
        {
            var winner = currentFlight.CancellationSubmissions.ShouldHaveSingleItem();
            winner.SubmittedByEmployee.ShouldBe(employee);
            winner.Details.ShouldBe(details);
            winner.IsAccepted.ShouldBeTrue();
            currentFlight.ArrivalSubmissions.ShouldBeEmpty();
            currentFlight.DepartureSubmissions.ShouldBeEmpty();
            SubmitOperation(currentFlight, isArrival, employee, matchingClock).Error.Code
                .ShouldBe("Operations.Flight.CancellationSubmitted");
        }
        else
        {
            currentFlight.CancellationSubmissions.ShouldBeEmpty();
            if (isArrival)
            {
                currentFlight.ArrivalSubmissions.ShouldHaveSingleItem().SubmittedByEmployee.ShouldBe(employee);
                currentFlight.DepartureSubmissions.ShouldBeEmpty();
            }
            else
            {
                currentFlight.DepartureSubmissions.ShouldHaveSingleItem().SubmittedByEmployee.ShouldBe(employee);
                currentFlight.ArrivalSubmissions.ShouldBeEmpty();
            }
            currentFlight.SubmitCancellation(employee, details, cancellationUserId, matchingClock).Error.Code
                .ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        }
        (await SaveAsync(db, synchronousSave)).ShouldBe(0);
        db.ChangeTracker.Clear();
        currentFlight = await LoadFlightAsync(db, flight.Id);
        currentFlight.RowVersion.ShouldBe(winnerVersion);
        currentFlight.UpdatedAtUtc.ShouldBe(matchingClock);
        currentFlight.CancellationSubmissions.Count.ShouldBe(cancellationWins ? 1 : 0);
        currentFlight.ArrivalSubmissions.Count.ShouldBe(!cancellationWins && isArrival ? 1 : 0);
        currentFlight.DepartureSubmissions.Count.ShouldBe(!cancellationWins && !isArrival ? 1 : 0);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Revert_cancellation_checks_the_aggregate_version_even_when_the_clock_does_not_change(bool revertWins, bool synchronousSave)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var employee = Employee("Canceling engineer", "E410");
        var other = Employee("Other engineer", "E411");
        var matchingClock = Now.AddMinutes(1);
        var canceled = flight.SubmitCancellation(employee, CancellationDetails.Create(Now, "First report").Value,
            Guid.NewGuid(), matchingClock).Value;
        var retained = flight.SubmitCancellation(other, CancellationDetails.Create(Now, "Other report").Value,
            Guid.NewGuid(), matchingClock).Value;
        db.Flights.Add(flight);
        await SaveAsync(db, synchronousSave);
        var before = flight.RowVersion.ToArray();
        db.ChangeTracker.Clear();
        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winningFlight = await LoadFlightAsync(db, flight.Id);
        var staleFlight = await LoadFlightAsync(competingDb, flight.Id);
        var reverting = revertWins ? winningFlight : staleFlight;
        var editing = revertWins ? staleFlight : winningFlight;
        reverting.RevertCancellation(employee.StaffMemberId, canceled.Id, matchingClock).IsSuccess.ShouldBeTrue();
        editing.ChangeFlightNumber(FlightNumber.Create("SV1021", "SV").Value, matchingClock).IsSuccess.ShouldBeTrue();
        await SaveAsync(db, synchronousSave);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => SaveAsync(competingDb, synchronousSave));
        db.ChangeTracker.Clear();
        var current = await LoadFlightAsync(db, flight.Id);
        current.RowVersion.SequenceEqual(before).ShouldBeFalse();
        current.UpdatedAtUtc.ShouldBe(matchingClock);
        current.CancellationSubmissions.Count.ShouldBe(revertWins ? 1 : 2);
        current.CancellationSubmissions.Single(c => c.Id == retained.Id).IsAccepted.ShouldBeFalse();
        current.CancellationSubmissions.Any(c => c.Id == canceled.Id).ShouldBe(!revertWins);
        current.FlightNumber.Value.ShouldBe(revertWins ? "SV1020" : "SV1021");
    }

    private static Task<Flight> LoadFlightAsync(OperationsDbContext db, Guid flightId) =>
        db.Flights
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .Include(item => item.CancellationSubmissions)
            .SingleAsync(item => item.Id == flightId);

    private static async Task<int> SaveAsync(OperationsDbContext db, bool synchronousSave) =>
        synchronousSave ? db.SaveChanges() : await db.SaveChangesAsync();

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

    private static Result SubmitOperation(Flight flight, bool isArrival, StaffMemberSnapshot employee, DateTimeOffset now)
    {
        if (isArrival)
        {
            var details = ArrivalDetails.Create("ARRIVAL-REFERENCE", Now, flight.AircraftType!, "HZ-123",
                employee, "Arrival recorded by engineer").Value;
            return flight.SubmitArrival(employee, details, Guid.NewGuid(), now);
        }

        var departure = DepartureDetails.Create(Now.AddHours(2),
            CustomerSignature.Create("departure.png", "signatures/departure.png", "image/png", 1024, Now.AddHours(2)).Value,
            employee, "Departure recorded by engineer").Value;
        return flight.SubmitDeparture(employee, departure, Guid.NewGuid(), now);
    }

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightCancellation_{Guid.NewGuid():N}"
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
