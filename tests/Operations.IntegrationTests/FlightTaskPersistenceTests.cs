using MasterData.Contracts.Resources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightTaskPersistenceTests(FlightTaskPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightTaskPersistenceTests.DatabaseFixture>
{
    private const string ServiceBaseline = "20261108201817_Operations_FlightServices";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Task_migration_preserves_planning_submissions_services_and_flight_versions()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(ServiceBaseline);
        var previousMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var actor = Guid.NewGuid();
        var employee = Employee();
        var flight = ScheduleFlight(employee);
        var arrival = flight.SubmitArrival(employee,
            ArrivalDetails.Create("ARRIVAL-REF", Now, flight.AircraftType!, "HZ-100", employee, "Arrival remarks").Value,
            actor, Now.AddMinutes(1)).Value;
        var departure = flight.SubmitDeparture(employee,
            DepartureDetails.Create(Now.AddHours(2), CustomerSignature.Create("signatures/task-migration.png", "image/png", 1024).Value,
                employee, "Departure remarks").Value,
            actor, Now.AddHours(2)).Value;
        var service = flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Actual service"), "SERVICE-REF",
            Window(Now.AddHours(1)), "Service notes", actor, Now.AddMinutes(2)).Value;
        var servicePerformer = flight.AddServicePerformer(service.Id, employee, Window(), actor, Now.AddMinutes(2)).Value;
        var serviceAttachments = new[]
        {
            (Id: Guid.NewGuid(), Details: Attachment()),
            (Id: Guid.NewGuid(), Details: AttachmentDetails.Create("voice.m4a", "tasks/voice.m4a", "audio/mp4", 2048, AttachmentKind.Voice).Value),
            (Id: Guid.NewGuid(), Details: AttachmentDetails.Create("report.pdf", "tasks/report.pdf", "application/pdf", 8192, AttachmentKind.Document).Value)
        };
        var canceled = ScheduleFlight();
        var cancellation = canceled.SubmitCancellation(employee, CancellationDetails.Create(Now, "Canceled").Value,
            actor, Now.AddMinutes(1)).Value;
        db.Flights.AddRange(flight, canceled);
        await db.SaveChangesAsync();
        // The published service schema predates Kind. Seed its original columns explicitly.
        foreach (var attachment in serviceAttachments)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [operations].[flight_service_attachments]
                    ([Id], [FlightServiceId], [FileName], [StorageKey], [ContentType], [SizeBytes], [UploadedByUserId], [UploadedAtUtc])
                VALUES ({attachment.Id}, {service.Id}, {attachment.Details.FileName}, {attachment.Details.StorageKey},
                    {attachment.Details.ContentType}, {attachment.Details.SizeBytes}, {actor}, {Now.AddMinutes(2)})
                """);
        var flightVersion = flight.RowVersion.ToArray();
        var canceledVersion = canceled.RowVersion.ToArray();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(previousMigrations.Length + 1);
        appliedMigrations.Take(previousMigrations.Length).ShouldBe(previousMigrations);
        appliedMigrations.Last().ShouldEndWith("_Operations_FlightTasks");
        var reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(flightVersion);
        reloaded.UpdatedAtUtc.ShouldBe(flight.UpdatedAtUtc);
        reloaded.Customer.ShouldBe(flight.Customer);
        reloaded.Station.ShouldBe(flight.Station);
        reloaded.OperationType.ShouldBe(flight.OperationType);
        reloaded.FlightNumber.ShouldBe(flight.FlightNumber);
        reloaded.Schedule.ShouldBe(flight.Schedule);
        reloaded.AircraftType.ShouldBe(flight.AircraftType);
        reloaded.CreatedByUserId.ShouldBe(flight.CreatedByUserId);
        reloaded.CreatedAtUtc.ShouldBe(flight.CreatedAtUtc);
        reloaded.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(flight.PlannedServices.Single().Id);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(employee);
        var savedArrival = reloaded.ArrivalSubmissions.ShouldHaveSingleItem();
        savedArrival.Id.ShouldBe(arrival.Id);
        savedArrival.Details.ShouldBe(arrival.Details);
        savedArrival.IsAccepted.ShouldBeTrue();
        var savedDeparture = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        savedDeparture.Id.ShouldBe(departure.Id);
        savedDeparture.Details.ShouldBe(departure.Details);
        savedDeparture.IsAccepted.ShouldBeTrue();
        var savedService = reloaded.Services.ShouldHaveSingleItem();
        savedService.Id.ShouldBe(service.Id);
        savedService.Service.ShouldBe(service.Service);
        savedService.CustomerReferenceNumber.ShouldBe("SERVICE-REF");
        savedService.Notes.ShouldBe("Service notes");
        savedService.Window.ShouldBe(service.Window);
        savedService.CreatedByUserId.ShouldBe(actor);
        savedService.EndedByUserId.ShouldBe(actor);
        savedService.UpdatedAtUtc.ShouldBe(service.UpdatedAtUtc);
        savedService.Performers.ShouldHaveSingleItem().Id.ShouldBe(servicePerformer.Id);
        savedService.Performers.Single().Employee.ShouldBe(employee);
        savedService.Attachments.Count.ShouldBe(serviceAttachments.Length);
        foreach (var attachment in serviceAttachments)
        {
            var persistedAttachment = savedService.Attachments.Single(item => item.Id == attachment.Id);
            persistedAttachment.Details.ShouldBe(attachment.Details);
            persistedAttachment.UploadedByUserId.ShouldBe(actor);
            persistedAttachment.UploadedAtUtc.ShouldBe(Now.AddMinutes(2));
        }
        reloaded.Tasks.ShouldBeEmpty();
        var reloadedCanceled = await LoadFlightAsync(db, canceled.Id);
        reloadedCanceled.RowVersion.ShouldBe(canceledVersion);
        reloadedCanceled.UpdatedAtUtc.ShouldBe(canceled.UpdatedAtUtc);
        reloadedCanceled.CancellationSubmissions.ShouldHaveSingleItem().Id.ShouldBe(cancellation.Id);
        reloadedCanceled.CancellationSubmissions.Single().Details.ShouldBe(cancellation.Details);
        reloadedCanceled.CancellationSubmissions.Single().IsAccepted.ShouldBeTrue();
        reloadedCanceled.Tasks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("duplicate-employees")]
    [InlineData("oversized-notes")]
    public async Task Task_migration_preflight_preserves_legacy_rows_when_service_data_needs_resolution(string invalidData)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(ServiceBaseline);
        var previousMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var service = flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Legacy service"), "LEGACY-REF", Window(),
            "Legacy notes", actor, Now.AddMinutes(1)).Value;
        var performer = flight.AddServicePerformer(service.Id, Employee(), Window(), actor, Now.AddMinutes(1)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var version = flight.RowVersion.ToArray();
        var attachmentId = Guid.NewGuid();
        var legacyFileName = "legacy/path/" + new string('x', 260) + ".jpg";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [operations].[flight_service_attachments]
                ([Id], [FlightServiceId], [FileName], [StorageKey], [ContentType], [SizeBytes], [UploadedByUserId], [UploadedAtUtc])
            VALUES ({attachmentId}, {service.Id}, {legacyFileName}, {"legacy/image.jpg"}, {"image/jpeg"}, {4096L}, {actor}, {Now})
            """);
        var legacyNotes = invalidData == "oversized-notes" ? new string('N', 2001) : "Legacy notes";
        var performerIds = new List<Guid> { performer.Id };
        if (invalidData == "oversized-notes")
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [operations].[flight_services] SET [Notes] = {legacyNotes} WHERE [Id] = {service.Id}
                """);
        else
        {
            var duplicateId = Guid.NewGuid();
            performerIds.Add(duplicateId);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [operations].[flight_service_performers]
                    ([Id], [FlightServiceId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc])
                SELECT {duplicateId}, [FlightServiceId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc]
                FROM [operations].[flight_service_performers] WHERE [Id] = {performer.Id}
                """);
        }
        db.ChangeTracker.Clear();

        var rejected = await Should.ThrowAsync<SqlException>(() => db.Database.MigrateAsync());

        rejected.Number.ShouldBe(51000);
        rejected.Message.ShouldContain("FlightServices parity");
        rejected.Message.ShouldContain(invalidData == "oversized-notes" ? "notes over 2000" : "duplicate employee");
        (await db.Database.GetAppliedMigrationsAsync()).ShouldBe(previousMigrations);
        (await db.Set<FlightService>().Where(item => item.Id == service.Id).Select(item => item.Notes).SingleAsync())
            .ShouldBe(legacyNotes);
        (await db.Set<FlightService>().Where(item => item.Id == service.Id).Select(item => item.CustomerReferenceNumber).SingleAsync())
            .ShouldBe("LEGACY-REF");
        (await db.Set<FlightServicePerformer>().Where(item => item.FlightServiceId == service.Id).Select(item => item.Id)
            .ToArrayAsync()).OrderBy(id => id).ShouldBe(performerIds.OrderBy(id => id));
        (await db.Set<FlightServiceAttachment>().Where(item => item.Id == attachmentId).Select(item => item.Details.FileName).SingleAsync())
            .ShouldBe(legacyFileName);
        (await db.Flights.Where(item => item.Id == flight.Id).Select(item => item.RowVersion).SingleAsync()).ShouldBe(version);
        await db.Database.OpenConnectionAsync();
        await using var schemaCheck = db.Database.GetDbConnection().CreateCommand();
        schemaCheck.CommandText = """
            SELECT COUNT(*) FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'[operations].[flight_service_attachments]') AND [name] = N'Kind'
            """;
        Convert.ToInt32(await schemaCheck.ExecuteScalarAsync()).ShouldBe(0);
        schemaCheck.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE [schema_id] = SCHEMA_ID(N'operations') AND [name] = N'flight_tasks'";
        Convert.ToInt32(await schemaCheck.ExecuteScalarAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Task_graph_round_trips_quantity_and_duration_resources_stable_edits_and_optional_ata_chapter()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var starter = Guid.NewGuid();
        var closer = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var employee = Employee();
        var otherEmployee = Employee();
        var flight = ScheduleFlight(employee);
        var task = flight.AddTask(FlightTaskType.Major, null, "  CUSTOMER-REF  ", Window(), "  Task notes  ",
            starter, Now.AddMinutes(1)).Value;
        var secondTaskRecord = flight.AddTask(FlightTaskType.Minor, null, null, Window(), null, starter, Now.AddMinutes(1)).Value;
        var tool = new ToolSnapshot(Guid.NewGuid(), "Torque wrench");
        var quantityTool = new ToolSnapshot(Guid.NewGuid(), "Tool kit", ResourceCalculationType.Quantity);
        var material = new MaterialSnapshot(Guid.NewGuid(), "Lubricant");
        var durationMaterial = new MaterialSnapshot(Guid.NewGuid(), "Circulating fluid", ResourceCalculationType.Duration);
        var support = new GeneralSupportSnapshot(Guid.NewGuid(), "Ground power");
        var durationSupport = new GeneralSupportSnapshot(Guid.NewGuid(), "Support crew", ResourceCalculationType.Duration);
        var partialWindow = TimeWindow.Create(Now.AddMinutes(5), Now.AddMinutes(10)).Value;
        var partialUsage = ResourceUsage.Create(ResourceCalculationType.Duration, null,
            partialWindow.FromUtc, partialWindow.ToUtc).Value;
        var firstTool = flight.AddTaskTool(task.Id, tool, Usage(), "  Tool notes  ", editor, Now.AddMinutes(2)).Value;
        var secondTool = flight.AddTaskTool(task.Id, quantityTool, Quantity(9999999999999999.99m), null, editor, Now.AddMinutes(2)).Value;
        var firstMaterial = flight.AddTaskMaterial(task.Id, material, Quantity(12.25m), "  Material notes  ", editor, Now.AddMinutes(2)).Value;
        var secondMaterial = flight.AddTaskMaterial(task.Id, durationMaterial, Usage(), null, editor, Now.AddMinutes(2)).Value;
        var firstSupport = flight.AddTaskGeneralSupport(task.Id, support, Quantity(3.50m), "  Support notes  ", editor, Now.AddMinutes(2)).Value;
        var secondSupport = flight.AddTaskGeneralSupport(task.Id, durationSupport, partialUsage, null, editor, Now.AddMinutes(2)).Value;
        var firstPerformer = flight.AddTaskPerformer(task.Id, employee, Window(), "  Employee notes  ", editor, Now.AddMinutes(2)).Value;
        var secondPerformer = flight.AddTaskPerformer(task.Id, otherEmployee, partialWindow, null, editor, Now.AddMinutes(2)).Value;
        var image = flight.AddTaskAttachment(task.Id, Attachment(), editor, Now.AddMinutes(3)).Value;
        var voiceDetails = AttachmentDetails.Create("voice.m4a", "tasks/voice.m4a", "audio/mp4", 2048, AttachmentKind.Voice).Value;
        var voice = flight.AddTaskAttachment(task.Id, voiceDetails, editor, Now.AddMinutes(3)).Value;
        var documentDetails = AttachmentDetails.Create("report.pdf", "tasks/report.pdf", "application/pdf", 8192, AttachmentKind.Document).Value;
        var document = flight.AddTaskAttachment(task.Id, documentDetails, editor, Now.AddMinutes(3)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.Tasks.Count.ShouldBe(2);
        reloaded.Services.ShouldBeEmpty();
        reloaded.PlannedServices.ShouldHaveSingleItem();
        var persisted = reloaded.Tasks.Single(item => item.Id == task.Id);
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.TaskType.ShouldBe(FlightTaskType.Major);
        persisted.AtaChapter.ShouldBeNull();
        persisted.CustomerReferenceNumber.ShouldBe("CUSTOMER-REF");
        persisted.Notes.ShouldBe("Task notes");
        persisted.Window.FromUtc.ShouldBe(Now);
        persisted.Window.FromUtc.Offset.ShouldBe(TimeSpan.Zero);
        persisted.Window.ToUtc.ShouldBeNull();
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBeNull();
        persisted.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        persisted.UpdatedByUserId.ShouldBe(editor);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        persisted.Tools.Count.ShouldBe(2);
        persisted.Tools.Single(item => item.Id == firstTool.Id).Tool.ShouldBe(tool);
        persisted.Tools.Single(item => item.Id == firstTool.Id).Notes.ShouldBe("Tool notes");
        persisted.Tools.Single(item => item.Id == firstTool.Id).Usage.ShouldBe(Usage());
        persisted.Tools.Single(item => item.Id == secondTool.Id).Tool.ShouldBe(quantityTool);
        persisted.Tools.Single(item => item.Id == secondTool.Id).Usage.ShouldBe(Quantity(9999999999999999.99m));
        persisted.Materials.Count.ShouldBe(2);
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Material.ShouldBe(material);
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Usage.ShouldBe(Quantity(12.25m));
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Notes.ShouldBe("Material notes");
        persisted.Materials.Single(item => item.Id == secondMaterial.Id).Material.ShouldBe(durationMaterial);
        persisted.Materials.Single(item => item.Id == secondMaterial.Id).Usage.ShouldBe(Usage());
        persisted.GeneralSupports.Count.ShouldBe(2);
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).GeneralSupport.ShouldBe(support);
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).Usage.ShouldBe(Quantity(3.50m));
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).Notes.ShouldBe("Support notes");
        persisted.GeneralSupports.Single(item => item.Id == secondSupport.Id).GeneralSupport.ShouldBe(durationSupport);
        persisted.GeneralSupports.Single(item => item.Id == secondSupport.Id).Usage.ShouldBe(partialUsage);
        persisted.Performers.Count.ShouldBe(2);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Employee.ShouldBe(employee);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Notes.ShouldBe("Employee notes");
        persisted.Performers.Single(item => item.Id == secondPerformer.Id).Employee.ShouldBe(otherEmployee);
        persisted.Attachments.Count.ShouldBe(3);
        persisted.Attachments.Single(item => item.Id == image.Id).Details.ShouldBe(image.Details);
        persisted.Attachments.Single(item => item.Id == voice.Id).Details.ShouldBe(voiceDetails);
        persisted.Attachments.Single(item => item.Id == document.Id).Details.ShouldBe(documentDetails);
        persisted.Attachments.ShouldAllBe(item => item.UploadedByUserId == editor && item.UploadedAtUtc == Now.AddMinutes(3));
        var secondTask = reloaded.Tasks.Single(item => item.Id == secondTaskRecord.Id);
        secondTask.TaskType.ShouldBe(FlightTaskType.Minor);
        secondTask.CustomerReferenceNumber.ShouldBeNull();
        secondTask.Notes.ShouldBeNull();
        var version = reloaded.RowVersion.ToArray();

        var chapter = new AtaChapterSnapshot(Guid.NewGuid(), "32", "Landing gear");
        var closedWindow = Window(Now.AddHours(2));
        var closedUsage = Usage(closedWindow.ToUtc);
        var changedTool = new ToolSnapshot(Guid.NewGuid(), "Calibrated torque wrench");
        var changedMaterial = new MaterialSnapshot(Guid.NewGuid(), "Approved lubricant");
        var changedSupport = new GeneralSupportSnapshot(Guid.NewGuid(), "External ground power");
        var changedEmployee = new StaffMemberSnapshot(Guid.NewGuid(), "Replacement employee", "E101");
        reloaded.UpdateTask(task.Id, FlightTaskType.Minor, chapter, "UPDATED-REF", closedWindow, "Updated task notes",
            closer, Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskTool(task.Id, firstTool.Id, changedTool, closedUsage, "Updated tool notes", editor, Now.AddMinutes(5))
            .IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskMaterial(task.Id, firstMaterial.Id, changedMaterial, Quantity(15.75m), "Updated material notes", editor, Now.AddMinutes(5))
            .IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskGeneralSupport(task.Id, firstSupport.Id, changedSupport, Quantity(4.25m), "Updated support notes", editor, Now.AddMinutes(5))
            .IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskPerformer(task.Id, firstPerformer.Id, changedEmployee, closedWindow, "Updated employee notes", editor, Now.AddMinutes(5))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Tasks.Single(item => item.Id == task.Id);
        persisted.TaskType.ShouldBe(FlightTaskType.Minor);
        persisted.AtaChapter.ShouldBe(chapter);
        persisted.CustomerReferenceNumber.ShouldBe("UPDATED-REF");
        persisted.Notes.ShouldBe("Updated task notes");
        persisted.Window.ShouldBe(closedWindow);
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.EndedByUserId.ShouldBe(closer);
        persisted.UpdatedByUserId.ShouldBe(editor);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(5));
        persisted.Tools.Single(item => item.Id == firstTool.Id).Tool.ShouldBe(changedTool);
        persisted.Tools.Single(item => item.Id == firstTool.Id).Usage.ShouldBe(closedUsage);
        persisted.Tools.Single(item => item.Id == firstTool.Id).Notes.ShouldBe("Updated tool notes");
        persisted.Tools.Single(item => item.Id == secondTool.Id).Tool.ShouldBe(quantityTool);
        persisted.Tools.Single(item => item.Id == secondTool.Id).Usage.ShouldBe(Quantity(9999999999999999.99m));
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Material.ShouldBe(changedMaterial);
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Usage.ShouldBe(Quantity(15.75m));
        persisted.Materials.Single(item => item.Id == firstMaterial.Id).Notes.ShouldBe("Updated material notes");
        persisted.Materials.Single(item => item.Id == secondMaterial.Id).Material.ShouldBe(durationMaterial);
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).GeneralSupport.ShouldBe(changedSupport);
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).Usage.ShouldBe(Quantity(4.25m));
        persisted.GeneralSupports.Single(item => item.Id == firstSupport.Id).Notes.ShouldBe("Updated support notes");
        persisted.GeneralSupports.Single(item => item.Id == secondSupport.Id).GeneralSupport.ShouldBe(durationSupport);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Employee.ShouldBe(changedEmployee);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Window.ShouldBe(closedWindow);
        persisted.Performers.Single(item => item.Id == firstPerformer.Id).Notes.ShouldBe("Updated employee notes");
        persisted.Performers.Single(item => item.Id == secondPerformer.Id).Employee.ShouldBe(otherEmployee);
        persisted.Attachments.Select(item => item.Id).OrderBy(id => id)
            .ShouldBe(new[] { image.Id, voice.Id, document.Id }.OrderBy(id => id));
        reloaded.RowVersion.SequenceEqual(version).ShouldBeFalse();

        reloaded.UpdateTask(task.Id, persisted.TaskType, null, persisted.CustomerReferenceNumber, persisted.Window,
            persisted.Notes, editor, Now.AddMinutes(6)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Tasks.Single(item => item.Id == task.Id);
        persisted.AtaChapter.ShouldBeNull();
        persisted.EndedByUserId.ShouldBe(closer);
        persisted.CreatedByUserId.ShouldBe(starter);
        persisted.Tools.Count.ShouldBe(2);
        persisted.Materials.Count.ShouldBe(2);
        persisted.GeneralSupports.Count.ShouldBe(2);
        persisted.Performers.Count.ShouldBe(2);
        reloaded.Tasks.Single(item => item.Id == secondTaskRecord.Id).Window.ToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_resource_modes_duplicates_and_period_bounds_leave_the_persisted_graph_unchanged()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var task = AddGraph(flight, actor, Now.AddMinutes(1));
        flight.UpdateTask(task.Id, task.TaskType, null, null, Window(Now.AddHours(1)), null, actor, Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Tasks.ShouldHaveSingleItem();
        var version = reloaded.RowVersion.ToArray();
        var tool = persisted.Tools.ShouldHaveSingleItem();
        reloaded.AddTaskTool(task.Id, tool.Tool, tool.Usage, null, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.AddTaskMaterial(task.Id, new MaterialSnapshot(Guid.NewGuid(), "Quantity material"), Usage(), null,
            actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.UpdateTaskTool(task.Id, tool.Id, tool.Tool, Quantity(1), null, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        var beforeParent = ResourceUsage.Create(ResourceCalculationType.Duration, null, Now.AddMinutes(-1), null).Value;
        reloaded.AddTaskTool(task.Id, new ToolSnapshot(Guid.NewGuid(), "Outside tool"), beforeParent, null,
            actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        var afterParent = Usage(Now.AddMinutes(61));
        reloaded.AddTaskGeneralSupport(task.Id, new GeneralSupportSnapshot(Guid.NewGuid(), "Outside support", ResourceCalculationType.Duration),
            afterParent, null, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.AddTaskPerformer(task.Id, persisted.Performers.Single().Employee, Window(), null, actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        var incompatibleParent = TimeWindow.Create(Now.AddMinutes(1), Now.AddHours(1)).Value;
        reloaded.UpdateTask(task.Id, FlightTaskType.Minor, new AtaChapterSnapshot(Guid.NewGuid(), "32", "Landing gear"),
            "Should not apply", incompatibleParent, "Should not apply", actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Quantity, 1.001m, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.QuantityPrecisionInvalid");
        ResourceUsage.Create(ResourceCalculationType.Quantity, 10000000000000000m, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.QuantityPrecisionInvalid");

        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        persisted = reloaded.Tasks.ShouldHaveSingleItem();
        persisted.TaskType.ShouldBe(FlightTaskType.Major);
        persisted.AtaChapter.ShouldBeNull();
        persisted.CustomerReferenceNumber.ShouldBeNull();
        persisted.Notes.ShouldBeNull();
        persisted.Window.ShouldBe(Window(Now.AddHours(1)));
        AssertGraphRetained(persisted, task);
        reloaded.RowVersion.ShouldBe(version);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("material")]
    [InlineData("support")]
    public async Task Resource_usage_database_checks_reject_mixed_branches_invalid_modes_and_reversed_periods(string kind)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var task = AddGraph(flight, Guid.NewGuid(), Now.AddMinutes(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var version = flight.RowVersion.ToArray();
        var id = kind switch
        {
            "tool" => task.Tools.Single().Id,
            "material" => task.Materials.Single().Id,
            _ => task.GeneralSupports.Single().Id
        };
        var quantitySql = kind switch
        {
            "tool" => "UPDATE [operations].[flight_task_tools] SET [Quantity] = 1 WHERE [Id] = @id",
            "material" => "UPDATE [operations].[flight_task_materials] SET [Quantity] = 1 WHERE [Id] = @id",
            _ => "UPDATE [operations].[flight_task_general_supports] SET [Quantity] = 1 WHERE [Id] = @id"
        };
        var modeSql = kind switch
        {
            "tool" => "UPDATE [operations].[flight_task_tools] SET [CalculationType] = @mode WHERE [Id] = @id",
            "material" => "UPDATE [operations].[flight_task_materials] SET [CalculationType] = @mode WHERE [Id] = @id",
            _ => "UPDATE [operations].[flight_task_general_supports] SET [CalculationType] = @mode WHERE [Id] = @id"
        };
        var reversedSql = kind switch
        {
            "tool" => "UPDATE [operations].[flight_task_tools] SET [ToUtc] = @end WHERE [Id] = @id",
            "material" => "UPDATE [operations].[flight_task_materials] SET [ToUtc] = @end WHERE [Id] = @id",
            _ => "UPDATE [operations].[flight_task_general_supports] SET [ToUtc] = @end WHERE [Id] = @id"
        };
        var mixed = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(quantitySql, new SqlParameter("@id", id)));
        mixed.Number.ShouldBe(547);
        foreach (var mode in new[] { 0, 2 })
        {
            var invalidMode = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(modeSql,
                new SqlParameter("@mode", mode), new SqlParameter("@id", id)));
            invalidMode.Number.ShouldBe(547);
        }
        var reversed = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(reversedSql,
            new SqlParameter("@end", Now.AddMinutes(-1)), new SqlParameter("@id", id)));
        reversed.Number.ShouldBe(547);
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Tasks.ShouldHaveSingleItem();
        persisted.Tools.ShouldHaveSingleItem().Usage.ShouldBe(Usage());
        persisted.Materials.ShouldHaveSingleItem().Usage.ShouldBe(Usage());
        persisted.GeneralSupports.ShouldHaveSingleItem().Usage.ShouldBe(Usage());
        reloaded.RowVersion.ShouldBe(version);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("material")]
    [InlineData("support")]
    [InlineData("employee")]
    public async Task Task_child_unique_indexes_reject_duplicate_catalog_or_staff_rows(string kind)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var task = AddGraph(flight, Guid.NewGuid(), Now.AddMinutes(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var id = kind switch
        {
            "tool" => task.Tools.Single().Id,
            "material" => task.Materials.Single().Id,
            "support" => task.GeneralSupports.Single().Id,
            _ => task.Performers.Single().Id
        };
        var sql = kind switch
        {
            "tool" => """
                INSERT INTO [operations].[flight_task_tools]
                    ([Id], [FlightTaskId], [ToolId], [ToolName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes])
                SELECT @newId, [FlightTaskId], [ToolId], [ToolName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes]
                FROM [operations].[flight_task_tools] WHERE [Id] = @id
                """,
            "material" => """
                INSERT INTO [operations].[flight_task_materials]
                    ([Id], [FlightTaskId], [MaterialId], [MaterialName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes])
                SELECT @newId, [FlightTaskId], [MaterialId], [MaterialName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes]
                FROM [operations].[flight_task_materials] WHERE [Id] = @id
                """,
            "support" => """
                INSERT INTO [operations].[flight_task_general_supports]
                    ([Id], [FlightTaskId], [GeneralSupportId], [GeneralSupportName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes])
                SELECT @newId, [FlightTaskId], [GeneralSupportId], [GeneralSupportName], [CalculationType], [Quantity], [FromUtc], [ToUtc], [Notes]
                FROM [operations].[flight_task_general_supports] WHERE [Id] = @id
                """,
            _ => """
                INSERT INTO [operations].[flight_task_performers]
                    ([Id], [FlightTaskId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc], [Notes])
                SELECT @newId, [FlightTaskId], [StaffMemberId], [StaffFullName], [StaffEmployeeId], [FromUtc], [ToUtc], [Notes]
                FROM [operations].[flight_task_performers] WHERE [Id] = @id
                """
        };

        var duplicate = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql,
            new SqlParameter("@newId", Guid.NewGuid()), new SqlParameter("@id", id)));
        new[] { 2601, 2627 }.ShouldContain(duplicate.Number);
        db.ChangeTracker.Clear();
        var reloaded = await LoadFlightAsync(db, flight.Id);
        AssertGraphRetained(reloaded.Tasks.ShouldHaveSingleItem(), task);
    }

    [Fact]
    public async Task Equivalent_task_and_child_updates_do_not_write_or_change_actors()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var task = AddGraph(flight, actor, Now.AddMinutes(1));
        flight.UpdateTask(task.Id, task.TaskType, task.AtaChapter, task.CustomerReferenceNumber, Window(Now.AddHours(1)),
            task.Notes, actor, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var persisted = reloaded.Tasks.ShouldHaveSingleItem();
        var editor = Guid.NewGuid();
        var version = reloaded.RowVersion.ToArray();
        reloaded.UpdateTask(task.Id, persisted.TaskType, persisted.AtaChapter, persisted.CustomerReferenceNumber,
            persisted.Window, persisted.Notes, editor, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        var tool = persisted.Tools.ShouldHaveSingleItem();
        var material = persisted.Materials.ShouldHaveSingleItem();
        var support = persisted.GeneralSupports.ShouldHaveSingleItem();
        var performer = persisted.Performers.ShouldHaveSingleItem();
        reloaded.UpdateTaskTool(task.Id, tool.Id, tool.Tool, tool.Usage, tool.Notes, editor, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskMaterial(task.Id, material.Id, material.Material, material.Usage, material.Notes, editor, Now.AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskGeneralSupport(task.Id, support.Id, support.GeneralSupport, support.Usage, support.Notes, editor, Now.AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        reloaded.UpdateTaskPerformer(task.Id, performer.Id, performer.Employee, performer.Window, performer.Notes, editor, Now.AddMinutes(2))
            .IsSuccess.ShouldBeTrue();

        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        persisted.CreatedByUserId.ShouldBe(actor);
        persisted.EndedByUserId.ShouldBe(actor);
        persisted.UpdatedByUserId.ShouldBe(actor);
        persisted.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        reloaded.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        db.ChangeTracker.Clear();
        reloaded = await LoadFlightAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(version);
        reloaded.Tasks.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().Id.ShouldBe(task.Attachments.Single().Id);
    }

    [Fact]
    public async Task Foreign_ids_and_scoped_removals_cannot_change_other_tasks_or_flights()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var first = AddGraph(flight, actor, Now.AddMinutes(1));
        var sibling = AddGraph(flight, actor, Now.AddMinutes(1));
        var otherFlight = ScheduleFlight();
        var foreign = AddGraph(otherFlight, actor, Now.AddMinutes(1));
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, flight.Id);
        var version = reloaded.RowVersion.ToArray();
        reloaded.UpdateTask(foreign.Id, foreign.TaskType, foreign.AtaChapter, null, Window(), null, actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateTaskTool(first.Id, sibling.Tools.Single().Id, sibling.Tools.Single().Tool, Usage(), null, actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateTaskMaterial(first.Id, sibling.Materials.Single().Id, sibling.Materials.Single().Material, Usage(), null, actor, Now.AddMinutes(2))
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateTaskGeneralSupport(first.Id, sibling.GeneralSupports.Single().Id, sibling.GeneralSupports.Single().GeneralSupport,
            Usage(), null, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.UpdateTaskPerformer(first.Id, sibling.Performers.Single().Id, sibling.Performers.Single().Employee,
            Window(), null, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.RemoveTaskAttachment(first.Id, sibling.Attachments.Single().Id, actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        reloaded.AddTaskAttachment(foreign.Id, Attachment(), actor, Now.AddMinutes(2)).IsFailure.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);

        reloaded.RemoveTaskTool(first.Id, first.Tools.Single().Id, actor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        reloaded.RemoveTaskMaterial(first.Id, first.Materials.Single().Id, actor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        reloaded.RemoveTaskGeneralSupport(first.Id, first.GeneralSupports.Single().Id, actor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        reloaded.RemoveTaskPerformer(first.Id, first.Performers.Single().Id, actor, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        var removedAttachment = reloaded.RemoveTaskAttachment(first.Id, first.Attachments.Single().Id, actor, Now.AddMinutes(3)).Value;
        removedAttachment.Details.ShouldBe(first.Attachments.Single().Details);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadFlightAsync(db, flight.Id);
        var empty = reloaded.Tasks.Single(item => item.Id == first.Id);
        empty.Tools.ShouldBeEmpty();
        empty.Materials.ShouldBeEmpty();
        empty.GeneralSupports.ShouldBeEmpty();
        empty.Performers.ShouldBeEmpty();
        empty.Attachments.ShouldBeEmpty();
        AssertGraphRetained(reloaded.Tasks.Single(item => item.Id == sibling.Id), sibling);
        var other = await LoadFlightAsync(db, otherFlight.Id);
        AssertGraphRetained(other.Tasks.ShouldHaveSingleItem(), foreign);
        db.Flights.Remove(reloaded);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Set<FlightTask>().AnyAsync(item => item.FlightId == flight.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskTool>().AnyAsync(item => item.FlightTaskId == sibling.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskMaterial>().AnyAsync(item => item.FlightTaskId == sibling.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskGeneralSupport>().AnyAsync(item => item.FlightTaskId == sibling.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskPerformer>().AnyAsync(item => item.FlightTaskId == sibling.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskAttachment>().AnyAsync(item => item.FlightTaskId == sibling.Id)).ShouldBeFalse();
        other = await LoadFlightAsync(db, otherFlight.Id);
        AssertGraphRetained(other.Tasks.ShouldHaveSingleItem(), foreign);
    }

    [Fact]
    public async Task Pending_cancellation_blocks_task_writes_and_existing_tasks_block_cancellation()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var canceled = ScheduleFlight();
        var details = CancellationDetails.Create(Now, null).Value;
        canceled.SubmitCancellation(Employee(), details, actor, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        canceled.SubmitCancellation(Employee(), details, actor, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        var operational = ScheduleFlight();
        var task = AddGraph(operational, actor, Now.AddMinutes(1));
        db.Flights.AddRange(canceled, operational);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadFlightAsync(db, canceled.Id);
        var version = reloaded.RowVersion.ToArray();
        reloaded.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        const string blocked = "Operations.Flight.CancellationSubmitted";
        reloaded.AddTask(task.TaskType, task.AtaChapter, null, Window(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateTask(task.Id, task.TaskType, task.AtaChapter, null, Window(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        var tool = task.Tools.Single();
        var material = task.Materials.Single();
        var support = task.GeneralSupports.Single();
        var performer = task.Performers.Single();
        reloaded.AddTaskTool(task.Id, tool.Tool, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateTaskTool(task.Id, tool.Id, tool.Tool, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveTaskTool(task.Id, tool.Id, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.AddTaskMaterial(task.Id, material.Material, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateTaskMaterial(task.Id, material.Id, material.Material, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveTaskMaterial(task.Id, material.Id, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.AddTaskGeneralSupport(task.Id, support.GeneralSupport, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateTaskGeneralSupport(task.Id, support.Id, support.GeneralSupport, Usage(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveTaskGeneralSupport(task.Id, support.Id, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.AddTaskPerformer(task.Id, performer.Employee, Window(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.UpdateTaskPerformer(task.Id, performer.Id, performer.Employee, Window(), null, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveTaskPerformer(task.Id, performer.Id, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.AddTaskAttachment(task.Id, Attachment(), actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        reloaded.RemoveTaskAttachment(task.Id, task.Attachments.Single().Id, actor, Now.AddMinutes(3)).Error.Code.ShouldBe(blocked);
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        reloaded.Tasks.ShouldBeEmpty();

        var active = await LoadFlightAsync(db, operational.Id);
        var activeVersion = active.RowVersion.ToArray();
        active.SubmitCancellation(Employee(), details, actor, Now.AddMinutes(3)).Error.Code
            .ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        (await db.SaveChangesAsync()).ShouldBe(0);
        active.RowVersion.ShouldBe(activeVersion);
        active.CancellationSubmissions.ShouldBeEmpty();
        active.Tasks.ShouldHaveSingleItem().Id.ShouldBe(task.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Matching_clock_task_and_cancellation_reject_stale_writes_in_both_save_paths(
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
        var actor = Guid.NewGuid();
        var employee = Employee();
        var cancellation = CancellationDetails.Create(Now, "Canceled by customer").Value;
        if (cancellationWins)
        {
            winner.SubmitCancellation(employee, cancellation, actor, matchingClock).IsSuccess.ShouldBeTrue();
            stale.AddTask(FlightTaskType.Major, null, null, Window(), null, actor, matchingClock).IsSuccess.ShouldBeTrue();
        }
        else
        {
            winner.AddTask(FlightTaskType.Major, null, null, Window(), null, actor, matchingClock).IsSuccess.ShouldBeTrue();
            stale.SubmitCancellation(employee, cancellation, actor, matchingClock).IsSuccess.ShouldBeTrue();
        }
        await SaveAsync(db, synchronousSave);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => SaveAsync(competingDb, synchronousSave));
        db.ChangeTracker.Clear();
        var current = await LoadFlightAsync(db, flight.Id);
        var version = current.RowVersion.ToArray();
        if (cancellationWins)
        {
            current.CancellationSubmissions.ShouldHaveSingleItem().Details.ShouldBe(cancellation);
            current.Tasks.ShouldBeEmpty();
            current.AddTask(FlightTaskType.Minor, null, null, Window(), null, actor, matchingClock).IsFailure.ShouldBeTrue();
        }
        else
        {
            current.Tasks.ShouldHaveSingleItem().TaskType.ShouldBe(FlightTaskType.Major);
            current.CancellationSubmissions.ShouldBeEmpty();
            current.SubmitCancellation(employee, cancellation, actor, matchingClock).IsFailure.ShouldBeTrue();
        }
        (await SaveAsync(db, synchronousSave)).ShouldBe(0);
        current.RowVersion.ShouldBe(version);
        current.UpdatedAtUtc.ShouldBe(matchingClock);
    }

    [Theory]
    [InlineData("tool-update")]
    [InlineData("material-update")]
    [InlineData("support-update")]
    [InlineData("performer-update")]
    [InlineData("tool-remove")]
    [InlineData("material-remove")]
    [InlineData("support-remove")]
    [InlineData("performer-remove")]
    [InlineData("attachment-remove")]
    [InlineData("ata-remove")]
    public async Task Nested_task_changes_force_flight_concurrency_when_audit_values_match(string mutation)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        var matchingClock = Now.AddMinutes(1);
        var actor = Guid.NewGuid();
        var task = AddGraph(flight, actor, matchingClock);
        if (mutation == "ata-remove")
            flight.UpdateTask(task.Id, task.TaskType, new AtaChapterSnapshot(Guid.NewGuid(), "32", "Landing gear"),
                task.CustomerReferenceNumber, task.Window, task.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var competingDb = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winner = await LoadFlightAsync(db, flight.Id);
        var stale = await LoadFlightAsync(competingDb, flight.Id);
        var version = winner.RowVersion.ToArray();
        var persisted = winner.Tasks.ShouldHaveSingleItem();
        persisted.UpdatedByUserId.ShouldBe(actor);
        persisted.UpdatedAtUtc.ShouldBe(matchingClock);
        winner.UpdatedAtUtc.ShouldBe(matchingClock);
        var tool = persisted.Tools.ShouldHaveSingleItem();
        var material = persisted.Materials.ShouldHaveSingleItem();
        var support = persisted.GeneralSupports.ShouldHaveSingleItem();
        var performer = persisted.Performers.ShouldHaveSingleItem();
        var attachmentId = persisted.Attachments.ShouldHaveSingleItem().Id;
        var closedWindow = Window(Now.AddHours(1));
        switch (mutation)
        {
            case "tool-update":
                winner.UpdateTaskTool(task.Id, tool.Id, tool.Tool, Usage(closedWindow.ToUtc), tool.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "material-update":
                winner.UpdateTaskMaterial(task.Id, material.Id, material.Material, Usage(closedWindow.ToUtc), material.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "support-update":
                winner.UpdateTaskGeneralSupport(task.Id, support.Id, support.GeneralSupport, Usage(closedWindow.ToUtc), support.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "performer-update":
                winner.UpdateTaskPerformer(task.Id, performer.Id, performer.Employee, closedWindow, performer.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "tool-remove":
                winner.RemoveTaskTool(task.Id, tool.Id, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "material-remove":
                winner.RemoveTaskMaterial(task.Id, material.Id, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "support-remove":
                winner.RemoveTaskGeneralSupport(task.Id, support.Id, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "performer-remove":
                winner.RemoveTaskPerformer(task.Id, performer.Id, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "ata-remove":
                persisted.AtaChapter.ShouldNotBeNull();
                winner.UpdateTask(task.Id, persisted.TaskType, null, persisted.CustomerReferenceNumber, persisted.Window,
                    persisted.Notes, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            default:
                winner.RemoveTaskAttachment(task.Id, attachmentId, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
        }
        var staleTask = stale.Tasks.ShouldHaveSingleItem();
        stale.UpdateTask(task.Id, staleTask.TaskType, staleTask.AtaChapter, staleTask.CustomerReferenceNumber,
            staleTask.Window, "Stale overwrite", actor, matchingClock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => competingDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var current = await LoadFlightAsync(db, flight.Id);
        current.RowVersion.SequenceEqual(version).ShouldBeFalse();
        current.UpdatedAtUtc.ShouldBe(matchingClock);
        var retained = current.Tasks.ShouldHaveSingleItem();
        retained.Notes.ShouldBeNull();
        retained.UpdatedByUserId.ShouldBe(actor);
        retained.UpdatedAtUtc.ShouldBe(matchingClock);
        retained.AtaChapter.ShouldBeNull();
        retained.Tools.Count.ShouldBe(mutation == "tool-remove" ? 0 : 1);
        retained.Materials.Count.ShouldBe(mutation == "material-remove" ? 0 : 1);
        retained.GeneralSupports.Count.ShouldBe(mutation == "support-remove" ? 0 : 1);
        retained.Performers.Count.ShouldBe(mutation == "performer-remove" ? 0 : 1);
        retained.Attachments.Count.ShouldBe(mutation == "attachment-remove" ? 0 : 1);
        if (mutation == "tool-update")
            retained.Tools.ShouldHaveSingleItem().Usage.ShouldBe(Usage(closedWindow.ToUtc));
        if (mutation == "material-update")
            retained.Materials.ShouldHaveSingleItem().Usage.ShouldBe(Usage(closedWindow.ToUtc));
        if (mutation == "support-update")
            retained.GeneralSupports.ShouldHaveSingleItem().Usage.ShouldBe(Usage(closedWindow.ToUtc));
        if (mutation == "performer-update")
            retained.Performers.ShouldHaveSingleItem().Window.ShouldBe(closedWindow);
    }

    private static void AssertGraphRetained(FlightTask actual, FlightTask expected)
    {
        actual.Id.ShouldBe(expected.Id);
        actual.Tools.ShouldHaveSingleItem().Id.ShouldBe(expected.Tools.Single().Id);
        actual.Materials.ShouldHaveSingleItem().Id.ShouldBe(expected.Materials.Single().Id);
        actual.GeneralSupports.ShouldHaveSingleItem().Id.ShouldBe(expected.GeneralSupports.Single().Id);
        actual.Performers.ShouldHaveSingleItem().Id.ShouldBe(expected.Performers.Single().Id);
        actual.Attachments.ShouldHaveSingleItem().Id.ShouldBe(expected.Attachments.Single().Id);
    }

    private static Task<Flight> LoadFlightAsync(OperationsDbContext db, Guid flightId) =>
        db.Flights
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .Include(item => item.ArrivalSubmissions)
            .Include(item => item.DepartureSubmissions)
            .Include(item => item.CancellationSubmissions)
            .Include(item => item.Services).ThenInclude(item => item.Performers)
            .Include(item => item.Services).ThenInclude(item => item.Attachments)
            .Include(item => item.Tasks).ThenInclude(item => item.Tools)
            .Include(item => item.Tasks).ThenInclude(item => item.Materials)
            .Include(item => item.Tasks).ThenInclude(item => item.GeneralSupports)
            .Include(item => item.Tasks).ThenInclude(item => item.Performers)
            .Include(item => item.Tasks).ThenInclude(item => item.Attachments)
            .AsSplitQuery()
            .SingleAsync(item => item.Id == flightId);

    private static FlightTask AddGraph(Flight flight, Guid actor, DateTimeOffset now)
    {
        var task = flight.AddTask(FlightTaskType.Major, null, null, Window(), null, actor, now).Value;
        flight.AddTaskTool(task.Id, new ToolSnapshot(Guid.NewGuid(), "Torque wrench"), Usage(), null, actor, now).IsSuccess.ShouldBeTrue();
        flight.AddTaskMaterial(task.Id, new MaterialSnapshot(Guid.NewGuid(), "Lubricant", ResourceCalculationType.Duration), Usage(), null, actor, now).IsSuccess.ShouldBeTrue();
        flight.AddTaskGeneralSupport(task.Id, new GeneralSupportSnapshot(Guid.NewGuid(), "Ground power", ResourceCalculationType.Duration), Usage(), null, actor, now)
            .IsSuccess.ShouldBeTrue();
        flight.AddTaskPerformer(task.Id, Employee(), Window(), null, actor, now).IsSuccess.ShouldBeTrue();
        flight.AddTaskAttachment(task.Id, Attachment(), actor, now).IsSuccess.ShouldBeTrue();
        return task;
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

    private static StaffMemberSnapshot Employee() => new(Guid.NewGuid(), "Performing engineer", "E100");

    private static TimeWindow Window(DateTimeOffset? end = null) =>
        TimeWindow.Create(Now.ToOffset(TimeSpan.FromHours(3)), end?.ToOffset(TimeSpan.FromHours(3))).Value;

    private static ResourceUsage Usage(DateTimeOffset? end = null) =>
        ResourceUsage.Create(ResourceCalculationType.Duration, null, Now.ToOffset(TimeSpan.FromHours(3)), end).Value;

    private static ResourceUsage Quantity(decimal quantity) =>
        ResourceUsage.Create(ResourceCalculationType.Quantity, quantity, null, null).Value;

    private static AttachmentDetails Attachment() =>
        AttachmentDetails.Create("task.jpg", "tasks/task.jpg", "image/jpeg", 4096, AttachmentKind.Image).Value;

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightTask_{Guid.NewGuid():N}"
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
