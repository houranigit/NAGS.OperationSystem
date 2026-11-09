using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightServicePersistenceTests(FlightServicePersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightServicePersistenceTests.DatabaseFixture>
{
    private const string CancellationBaseline = "20261108190121_Operations_FlightCancellationSubmissions";
    private const string ServiceMigration = "20261108201817_Operations_FlightServices";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Service_migration_preserves_planning_and_all_submission_types_with_initially_empty_services()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(CancellationBaseline);
        var previousMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var employee = Employee("Assigned engineer", "E100");
        var operationalFlight = ScheduleFlight(employee);
        var arrivalDetails = ArrivalDetails.Create("ARRIVAL-REF", Now, operationalFlight.AircraftType!,
            "HZ-100", employee, "Arrival remarks").Value;
        var arrival = operationalFlight.SubmitArrival(employee, arrivalDetails, Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var departureDetails = DepartureDetails.Create(Now.AddHours(2),
            CustomerSignature.Create("signatures/migration.png", "image/png", 1024).Value,
            employee, "Departure remarks").Value;
        var departure = operationalFlight.SubmitDeparture(employee, departureDetails, Guid.NewGuid(), Now.AddHours(2)).Value;
        var canceledFlight = ScheduleFlight();
        var firstCancellation = canceledFlight.SubmitCancellation(employee,
            CancellationDetails.Create(Now, "First cancellation input").Value, Guid.NewGuid(), Now.AddMinutes(1)).Value;
        var secondCancellation = canceledFlight.SubmitCancellation(Employee("Second engineer", "E101"),
            CancellationDetails.Create(Now.AddMinutes(1), null).Value, Guid.NewGuid(), Now.AddMinutes(2)).Value;
        await PreRtrMigrationSeed.SaveAsync(db, operationalFlight, canceledFlight);
        var operationalVersion = operationalFlight.RowVersion.ToArray();
        var canceledVersion = canceledFlight.RowVersion.ToArray();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync(ServiceMigration);

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(previousMigrations.Length + 1);
        appliedMigrations.Take(previousMigrations.Length).ShouldBe(previousMigrations);
        appliedMigrations.Last().ShouldEndWith("_Operations_FlightServices");
        // Current mappings include metadata added after this published historical migration.
        await db.Database.MigrateAsync();
        var operational = await LoadFlightAsync(db, operationalFlight.Id);
        operational.Customer.ShouldBe(operationalFlight.Customer);
        operational.Station.ShouldBe(operationalFlight.Station);
        operational.OperationType.ShouldBe(operationalFlight.OperationType);
        operational.FlightNumber.ShouldBe(operationalFlight.FlightNumber);
        operational.Schedule.ShouldBe(operationalFlight.Schedule);
        operational.AircraftType.ShouldBe(operationalFlight.AircraftType);
        operational.CreatedByUserId.ShouldBe(operationalFlight.CreatedByUserId);
        operational.CreatedAtUtc.ShouldBe(operationalFlight.CreatedAtUtc);
        operational.UpdatedAtUtc.ShouldBe(operationalFlight.UpdatedAtUtc);
        operational.RowVersion.ShouldBe(operationalVersion);
        operational.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(operationalFlight.PlannedServices.Single().Id);
        var assignment = operational.AssignedEmployees.ShouldHaveSingleItem();
        assignment.Id.ShouldBe(operationalFlight.AssignedEmployees.Single().Id);
        assignment.Employee.ShouldBe(employee);
        var preservedArrival = operational.ArrivalSubmissions.ShouldHaveSingleItem();
        preservedArrival.Id.ShouldBe(arrival.Id);
        preservedArrival.Details.ShouldBe(arrivalDetails);
        preservedArrival.IsAccepted.ShouldBeTrue();
        var preservedDeparture = operational.DepartureSubmissions.ShouldHaveSingleItem();
        preservedDeparture.Id.ShouldBe(departure.Id);
        preservedDeparture.Details.ShouldBe(departureDetails);
        preservedDeparture.IsAccepted.ShouldBeTrue();
        operational.Services.ShouldBeEmpty();
        var canceled = await LoadFlightAsync(db, canceledFlight.Id);
        canceled.RowVersion.ShouldBe(canceledVersion);
        canceled.UpdatedAtUtc.ShouldBe(canceledFlight.UpdatedAtUtc);
        canceled.CancellationSubmissions.Count.ShouldBe(2);
        canceled.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        canceled.CancellationSubmissions.Single(item => item.Id == firstCancellation.Id).Details.ShouldBe(firstCancellation.Details);
        canceled.CancellationSubmissions.Single(item => item.Id == secondCancellation.Id).Details.ShouldBe(secondCancellation.Details);
        canceled.Services.ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_graph_round_trips_repeated_catalog_and_distinct_employees_and_keeps_child_ids_on_edits()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var employee = Employee("Assigned performing engineer", "E200");
        var flight = ScheduleFlight(employee);
        var catalog = flight.PlannedServices.Single().Service;
        var openWindow = TimeWindow.Create(Now.ToOffset(TimeSpan.FromHours(3)), null).Value;
        var createdBy = Guid.NewGuid();
        var performerUser = Guid.NewGuid();
        var uploadedBy = Guid.NewGuid();
        var service = flight.AddService(catalog, "  CUSTOMER-REF  ", openWindow, "  Service notes  ", createdBy, Now.AddMinutes(1)).Value;
        var repeatedService = flight.AddService(catalog, null, openWindow, null, createdBy, Now.AddMinutes(1)).Value;
        var firstPerformer = flight.AddServicePerformer(service.Id, employee, openWindow, performerUser, Now.AddMinutes(2)).Value;
        var earlierPeriod = TimeWindow.Create(Now.AddMinutes(5), Now.AddMinutes(10)).Value;
        var secondEmployee = Employee("Second performing engineer", "E201");
        var secondPerformer = flight.AddServicePerformer(service.Id, secondEmployee, earlierPeriod, performerUser, Now.AddMinutes(2)).Value;
        var image = Attachment("service.jpg", "services/service.jpg", "image/jpeg", 4096);
        var voice = Attachment("voice.m4a", "services/voice.m4a", "audio/mp4", 2048, AttachmentKind.Voice);
        var firstAttachment = flight.AddServiceAttachment(service.Id, image, uploadedBy,
            Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(3))).Value;
        var secondAttachment = flight.AddServiceAttachment(service.Id, voice, uploadedBy, Now.AddMinutes(3)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.Services.Count.ShouldBe(2);
        reloaded.Services.ShouldAllBe(item => item.Service.ServiceId == catalog.ServiceId);
        reloaded.PlannedServices.ShouldHaveSingleItem().Service.ShouldBe(catalog);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(employee);
        var persisted = reloaded.Services.Single(item => item.Id == service.Id);
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.Service.ShouldBe(catalog);
        persisted.CustomerReferenceNumber.ShouldBe("CUSTOMER-REF");
        persisted.Notes.ShouldBe("Service notes");
        persisted.Window.FromUtc.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Window.ToUtc.ShouldBeNull();
        persisted.EndedByUserId.ShouldBeNull();
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBe(uploadedBy);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        persisted.Performers.Count.ShouldBe(2);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Employee.ShouldBe(employee);
        persisted.Performers.Single(item => item.Id == secondPerformer.Id).Employee.ShouldBe(secondEmployee);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Window.ShouldBe(openWindow);
        persisted.Performers.Single(item => item.Id == secondPerformer.Id).Window.ShouldBe(earlierPeriod);
        persisted.Attachments.Count.ShouldBe(2);
        var savedImage = persisted.Attachments.Single(item => item.Id == firstAttachment.Id);
        savedImage.FlightServiceId.ShouldBe(service.Id);
        savedImage.Details.ShouldBe(image);
        savedImage.UploadedByUserId.ShouldBe(uploadedBy);
        savedImage.UploadedAtUtc.ShouldBe(Now.AddMinutes(3));
        savedImage.UploadedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Attachments.Single(item => item.Id == secondAttachment.Id).Details.ShouldBe(voice);
        var repeated = reloaded.Services.Single(item => item.Id == repeatedService.Id);
        repeated.CustomerReferenceNumber.ShouldBeNull();
        repeated.Notes.ShouldBeNull();
        var versionBeforeUpdate = reloaded.RowVersion.ToArray();

        var updatedBy = Guid.NewGuid();
        var performerEditor = Guid.NewGuid();
        var replacementEmployee = Employee("Replacement engineer", "E202");
        var changedCatalog = new ServiceSnapshot(Guid.NewGuid(), "Updated service selection");
        var closedWindow = TimeWindow.Create(Now, Now.AddHours(2)).Value;
        var closedPerformerWindow = TimeWindow.Create(Now, Now.AddHours(1)).Value;
        reloaded.UpdateService(service.Id, changedCatalog, "UPDATED-REF", closedWindow, "Updated notes",
            updatedBy, Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();
        reloaded.UpdateServicePerformer(service.Id, firstPerformer.Id, replacementEmployee, closedPerformerWindow,
            performerEditor, Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.Single(item => item.Id == service.Id);
        persisted.Service.ShouldBe(changedCatalog);
        persisted.Window.ShouldBe(closedWindow);
        persisted.EndedByUserId.ShouldBe(updatedBy);
        persisted.CustomerReferenceNumber.ShouldBe("UPDATED-REF");
        persisted.Notes.ShouldBe("Updated notes");
        persisted.CreatedByUserId.ShouldBe(createdBy);
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBe(performerEditor);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(5));
        persisted.Performers.Select(item => item.Id).OrderBy(id => id)
            .ShouldBe(new[] { firstPerformer.Id, secondPerformer.Id }.OrderBy(id => id));
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Employee.ShouldBe(replacementEmployee);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Window.ShouldBe(closedPerformerWindow);
        persisted.Performers.Single(item => item.Id == secondPerformer.Id).Window.ShouldBe(earlierPeriod);
        persisted.Attachments.Select(item => item.Id).OrderBy(id => id)
            .ShouldBe(new[] { firstAttachment.Id, secondAttachment.Id }.OrderBy(id => id));
        persisted.Attachments.Single(item => item.Id == firstAttachment.Id).Details.ShouldBe(image);
        reloaded.PlannedServices.ShouldHaveSingleItem().Service.ShouldBe(catalog);
        reloaded.RowVersion.SequenceEqual(versionBeforeUpdate).ShouldBeFalse();
    }

    [Fact]
    public async Task Service_window_actors_survive_other_edits_and_follow_end_time_corrections()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var starter = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var attachmentEditor = Guid.NewGuid();
        var employeeEditor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var closedWindow = TimeWindow.Create(Now, Now.AddHours(1)).Value;
        var service = flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Completed service"), null,
            closedWindow, null, starter, Now.AddMinutes(1)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBe(starter);
        // A start correction, notes, and nested edits do not identify a new closer.
        var correctedStart = TimeWindow.Create(Now.AddMinutes(-10), closedWindow.ToUtc).Value;
        reloaded.UpdateService(service.Id, persisted.Service, null, correctedStart, "Corrected notes",
            editor, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        reloaded.AddServiceAttachment(service.Id, Attachment(), attachmentEditor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        reloaded.AddServicePerformer(service.Id, Employee("Performing engineer", "E250"), Window(),
            employeeEditor, Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBe(starter);
        persisted.UpdatedByUserId.ShouldBe(employeeEditor);
        var version = reloaded.RowVersion.ToArray();
        reloaded.UpdateService(service.Id, persisted.Service, null, persisted.Window, persisted.Notes,
            Guid.NewGuid(), Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        persisted.EndedByUserId.ShouldBe(starter);

        var correctedEnd = TimeWindow.Create(persisted.Window.FromUtc, Now.AddHours(2)).Value;
        reloaded.UpdateService(service.Id, persisted.Service, null, correctedEnd, persisted.Notes,
            editor, Now.AddMinutes(6)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBe(editor);
        persisted.Window.ShouldBe(correctedEnd);
        var openWindow = TimeWindow.Create(persisted.Window.FromUtc, null).Value;
        reloaded.UpdateService(service.Id, persisted.Service, null, openWindow, persisted.Notes,
            attachmentEditor, Now.AddMinutes(7)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBeNull();
        persisted.Window.ToUtc.ShouldBeNull();
        reloaded.UpdateService(service.Id, persisted.Service, null, correctedEnd, persisted.Notes,
            employeeEditor, Now.AddMinutes(8)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBe(employeeEditor);
        persisted.Window.ShouldBe(correctedEnd);
        persisted.Performers.ShouldHaveSingleItem();
        persisted.Attachments.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Foreign_ids_are_rejected_and_removing_children_or_flight_keeps_other_owners_intact()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var employee = Employee("Performing engineer", "E300");
        var flight = ScheduleFlight();
        var first = AddGraph(flight, employee, actor, Now.AddMinutes(1));
        var second = AddGraph(flight, employee, actor, Now.AddMinutes(1));
        var otherFlight = ScheduleFlight();
        var foreign = AddGraph(otherFlight, employee, actor, Now.AddMinutes(1));
        var firstPerformerId = first.Performers.Single().Id;
        var firstAttachment = first.Attachments.Single();
        var secondPerformerId = second.Performers.Single().Id;
        var secondAttachmentId = second.Attachments.Single().Id;
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var rowVersion = reloaded.RowVersion.ToArray();
        reloaded.UpdateService(foreign.Id, foreign.Service, null, Window(), null, actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateServicePerformer(first.Id, secondPerformerId, employee, Window(), actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        reloaded.RemoveServicePerformer(first.Id, secondPerformerId, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.RemoveServiceAttachment(first.Id, secondAttachmentId, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.AddServiceAttachment(foreign.Id, Attachment(), actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(rowVersion);

        reloaded.RemoveServicePerformer(first.Id, firstPerformerId, actor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        var removed = reloaded.RemoveServiceAttachment(first.Id, firstAttachment.Id, actor, Now.AddMinutes(3)).Value;
        removed.Id.ShouldBe(firstAttachment.Id);
        removed.Details.ShouldBe(firstAttachment.Details);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.Services.Single(item => item.Id == first.Id).Performers.ShouldBeEmpty();
        reloaded.Services.Single(item => item.Id == first.Id).Attachments.ShouldBeEmpty();
        var retained = reloaded.Services.Single(item => item.Id == second.Id);
        retained.Performers.ShouldHaveSingleItem().Id.ShouldBe(secondPerformerId);
        retained.Attachments.ShouldHaveSingleItem().Id.ShouldBe(secondAttachmentId);
        db.Flights.Remove(reloaded);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Set<FlightService>().AnyAsync(item => item.FlightId == flight.Id)).ShouldBeFalse();
        (await db.Set<FlightServicePerformer>().AnyAsync(item => item.FlightServiceId == second.Id)).ShouldBeFalse();
        (await db.Set<FlightServiceAttachment>().AnyAsync(item => item.FlightServiceId == second.Id)).ShouldBeFalse();
        var other = await LoadFlightAsync(db, otherFlight.Id);
        var preservedForeign = other.Services.ShouldHaveSingleItem();
        preservedForeign.Id.ShouldBe(foreign.Id);
        preservedForeign.Performers.ShouldHaveSingleItem().Id.ShouldBe(foreign.Performers.Single().Id);
        preservedForeign.Attachments.ShouldHaveSingleItem().Id.ShouldBe(foreign.Attachments.Single().Id);
    }

    [Fact]
    public async Task Service_employee_uniqueness_and_invalid_parent_edits_preserve_the_existing_graph()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var employee = Employee("Performing engineer", "E350");
        var service = AddGraph(flight, employee, actor, Now.AddMinutes(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Services.ShouldHaveSingleItem();
        var version = reloaded.RowVersion.ToArray();
        reloaded.AddServicePerformer(service.Id, employee, Window(), actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        var incompatibleParent = TimeWindow.Create(Now.AddMinutes(1), null).Value;
        reloaded.UpdateService(service.Id, persisted.Service, "Should not apply", incompatibleParent, "Should not apply",
            actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.UpdateService(service.Id, persisted.Service, new string('R', 201), persisted.Window, persisted.Notes,
            actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.UpdateService(service.Id, persisted.Service, persisted.CustomerReferenceNumber, persisted.Window,
            new string('N', 2001), actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        var performerId = persisted.Performers.ShouldHaveSingleItem().Id;
        var duplicate = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [operations].[flight_service_performers]
                ([Id], [FlightServiceId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc])
            SELECT {Guid.NewGuid()}, [FlightServiceId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc]
            FROM [operations].[flight_service_performers] WHERE [Id] = {performerId}
            """));
        new[] { 2601, 2627 }.ShouldContain(duplicate.Number);
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Services.ShouldHaveSingleItem();
        persisted.CustomerReferenceNumber.ShouldBeNull();
        persisted.Notes.ShouldBeNull();
        persisted.Window.ShouldBe(Window());
        persisted.Performers.ShouldHaveSingleItem().Id.ShouldBe(performerId);
        persisted.Performers.Single().Employee.ShouldBe(employee);
        persisted.Attachments.ShouldHaveSingleItem().Id.ShouldBe(service.Attachments.Single().Id);
        reloaded.RowVersion.ShouldBe(version);
    }

    [Fact]
    public async Task Equivalent_service_and_performer_updates_do_not_write_or_change_audit_metadata()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var service = AddGraph(flight, Employee("Performing engineer", "E400"), actor, Now.AddMinutes(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Services.ShouldHaveSingleItem();
        var performer = persisted.Performers.ShouldHaveSingleItem();
        var rowVersion = reloaded.RowVersion.ToArray();
        reloaded.UpdateService(service.Id, persisted.Service, persisted.CustomerReferenceNumber, persisted.Window,
            persisted.Notes, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        reloaded.UpdateServicePerformer(service.Id, performer.Id, performer.Employee, performer.Window,
            Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(rowVersion);
        persisted.UpdatedByUserId.ShouldBe(actor);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.CreatedByUserId.ShouldBe(actor);
        persisted.EndedByUserId.ShouldBeNull();
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(rowVersion);
        reloaded.Services.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().Id
            .ShouldBe(service.Attachments.Single().Id);
    }

    [Fact]
    public async Task Pending_cancellation_blocks_service_mutations_and_existing_services_block_cancellation()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var employee = Employee("Submitting engineer", "E500");
        var canceled = ScheduleFlight();
        var cancellationDetails = CancellationDetails.Create(Now, null).Value;
        canceled.SubmitCancellation(employee, cancellationDetails, actor, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        canceled.SubmitCancellation(Employee("Second submitting engineer", "E501"), cancellationDetails,
            actor, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        var operational = ScheduleFlight();
        var existingService = AddGraph(operational, employee, actor, Now.AddMinutes(1));
        db.Flights.AddRange(canceled, operational);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, canceled.Id);
        var rowVersion = reloaded.RowVersion.ToArray();
        var absentServiceId = Guid.NewGuid();
        const string blocked = "Operations.Flight.CancellationSubmitted";
        reloaded.AddService(existingService.Service, null, Window(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateService(absentServiceId, existingService.Service, null, Window(), null, actor, Now.AddMinutes(3))
            .Error.Code.ShouldBe(blocked);
        reloaded.AddServicePerformer(absentServiceId, employee, Window(), actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateServicePerformer(absentServiceId, Guid.NewGuid(), employee, Window(), actor, Now.AddMinutes(3))
            .Error.Code.ShouldBe(blocked);
        reloaded.RemoveServicePerformer(absentServiceId, Guid.NewGuid(), actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.AddServiceAttachment(absentServiceId, Attachment(), actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveServiceAttachment(absentServiceId, Guid.NewGuid(), actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.Services.ShouldBeEmpty();
        reloaded.CancellationSubmissions.Count.ShouldBe(2);
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(rowVersion);

        var active = await LoadFlightAsync(db, operational.Id);
        var activeVersion = active.RowVersion.ToArray();
        active.SubmitCancellation(employee, cancellationDetails, actor, Now.AddMinutes(3)).Error.Code
            .ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        (await db.SaveChangesAsync()).ShouldBe(0);
        active.RowVersion.ShouldBe(activeVersion);
        active.CancellationSubmissions.ShouldBeEmpty();
        active.Services.ShouldHaveSingleItem().Id.ShouldBe(existingService.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Matching_clock_service_and_cancellation_reject_stale_writes_in_both_save_paths(
        bool cancellationWins, bool synchronousSave)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var matchingClock = Now.AddMinutes(1);
        flight.UpdatePlanning(flight.Schedule, flight.AircraftType,
            flight.PlannedServices.Select(item => item.Service).ToArray(), null, matchingClock).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await SaveAsync(db, synchronousSave);
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winner = await LoadFlightAsync(db, flight.Id);
        var stale = await LoadFlightAsync(competingDb, flight.Id);
        winner.UpdatedAtUtc.ShouldBe(matchingClock);
        stale.UpdatedAtUtc.ShouldBe(matchingClock);
        var employee = Employee("Submitting engineer", "E600");
        var actor = Guid.NewGuid();
        var cancellationDetails = CancellationDetails.Create(Now, "Canceled by customer").Value;
        var catalog = new ServiceSnapshot(Guid.NewGuid(), "Actual service");
        if (cancellationWins)
        {
            winner.SubmitCancellation(employee, cancellationDetails, actor, matchingClock).IsSuccess.ShouldBeTrue();
            stale.AddService(catalog, null, Window(), null, actor, matchingClock).IsSuccess.ShouldBeTrue();
        }
        else
        {
            winner.AddService(catalog, null, Window(), null, actor, matchingClock).IsSuccess.ShouldBeTrue();
            stale.SubmitCancellation(employee, cancellationDetails, actor, matchingClock).IsSuccess.ShouldBeTrue();
        }
        await SaveAsync(db, synchronousSave);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => SaveAsync(competingDb, synchronousSave));
        db.ChangeTracker.Clear();
        var current = await LoadFlightAsync(db, flight.Id);
        var winnerVersion = current.RowVersion.ToArray();
        if (cancellationWins)
        {
            current.CancellationSubmissions.ShouldHaveSingleItem().Details.ShouldBe(cancellationDetails);
            current.Services.ShouldBeEmpty();
            current.AddService(catalog, null, Window(), null, actor, matchingClock).Error.Code
                .ShouldBe("Operations.Flight.CancellationSubmitted");
        }
        else
        {
            current.Services.ShouldHaveSingleItem().Service.ShouldBe(catalog);
            current.CancellationSubmissions.ShouldBeEmpty();
            current.SubmitCancellation(employee, cancellationDetails, actor, matchingClock).Error.Code
                .ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        }
        (await SaveAsync(db, synchronousSave)).ShouldBe(0);
        current.RowVersion.ShouldBe(winnerVersion);
        current.UpdatedAtUtc.ShouldBe(matchingClock);
    }

    [Theory]
    [InlineData("performer-update")]
    [InlineData("performer-remove")]
    [InlineData("attachment-remove")]
    public async Task Nested_only_service_changes_advance_flight_version_even_when_audit_values_match(string mutation)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var matchingClock = Now.AddMinutes(1);
        var actor = Guid.NewGuid();
        var employee = Employee("Performing engineer", "E700");
        var service = AddGraph(flight, employee, actor, matchingClock);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winner = await LoadFlightAsync(db, flight.Id);
        var stale = await LoadFlightAsync(competingDb, flight.Id);
        var originalVersion = winner.RowVersion.ToArray();
        var persisted = winner.Services.ShouldHaveSingleItem();
        persisted.UpdatedByUserId.ShouldBe(actor);
        persisted.UpdatedAtUtc.ShouldBe(matchingClock);
        winner.UpdatedAtUtc.ShouldBe(matchingClock);
        var performerId = persisted.Performers.ShouldHaveSingleItem().Id;
        var attachmentId = persisted.Attachments.ShouldHaveSingleItem().Id;
        var closedWindow = TimeWindow.Create(Now, Now.AddHours(1)).Value;
        if (mutation == "performer-update")
            winner.UpdateServicePerformer(service.Id, performerId, employee, closedWindow, actor, matchingClock).IsSuccess.ShouldBeTrue();
        else if (mutation == "performer-remove")
            winner.RemoveServicePerformer(service.Id, performerId, actor, matchingClock).IsSuccess.ShouldBeTrue();
        else
            winner.RemoveServiceAttachment(service.Id, attachmentId, actor, matchingClock).IsSuccess.ShouldBeTrue();
        var staleService = stale.Services.ShouldHaveSingleItem();
        stale.UpdateService(service.Id, staleService.Service, staleService.CustomerReferenceNumber, staleService.Window,
            "Stale overwrite", actor, matchingClock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => competingDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var current = await LoadFlightAsync(db, flight.Id);
        current.RowVersion.SequenceEqual(originalVersion).ShouldBeFalse();
        current.UpdatedAtUtc.ShouldBe(matchingClock);
        var retained = current.Services.ShouldHaveSingleItem();
        retained.Notes.ShouldBeNull();
        retained.UpdatedByUserId.ShouldBe(actor);
        retained.UpdatedAtUtc.ShouldBe(matchingClock);
        if (mutation == "performer-update")
        {
            retained.Performers.ShouldHaveSingleItem().Window.ShouldBe(closedWindow);
            retained.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachmentId);
        }
        else if (mutation == "performer-remove")
        {
            retained.Performers.ShouldBeEmpty();
            retained.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachmentId);
        }
        else
        {
            retained.Performers.ShouldHaveSingleItem().Id.ShouldBe(performerId);
            retained.Attachments.ShouldBeEmpty();
        }
    }

    private static Task<Flight> LoadFlightAsync(OperationsDbContext db, Guid flightId) =>
        db.Flights
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .Include(item => item.CancellationSubmissions)
            .Include(item => item.ServiceRecords).ThenInclude(item => item.Performers)
            .Include(item => item.ServiceRecords).ThenInclude(item => item.Attachments)
            .AsSplitQuery()
            .SingleAsync(item => item.Id == flightId);

    private static FlightService AddGraph(Flight flight, StaffMemberSnapshot employee, Guid actor, DateTimeOffset now)
    {
        var service = flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Actual service"), null, Window(), null, actor, now).Value;
        flight.AddServicePerformer(service.Id, employee, Window(), actor, now).IsSuccess.ShouldBeTrue();
        flight.AddServiceAttachment(service.Id, Attachment(), actor, now).IsSuccess.ShouldBeTrue();
        return service;
    }

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

    private static TimeWindow Window() => TimeWindow.Create(Now.ToOffset(TimeSpan.FromHours(3)), null).Value;

    private static AttachmentDetails Attachment(string fileName = "service.jpg", string storageKey = "services/service.jpg",
        string contentType = "image/jpeg", long sizeBytes = 4096, AttachmentKind kind = AttachmentKind.Image) =>
        AttachmentDetails.Create(fileName, storageKey, contentType, sizeBytes, kind).Value;

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightService_{Guid.NewGuid():N}"
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
