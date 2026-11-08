using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightTaskTests
{
    [Theory]
    [InlineData(FlightTaskType.Major, null, null, null, null)]
    [InlineData(FlightTaskType.Minor, " \t ", " ", null, null)]
    [InlineData(FlightTaskType.Major, " REF-42 ", " Work in progress ", "REF-42", "Work in progress")]
    public void AddTask_RecordsActualWorkAndOptionalText_WithoutChangingServicesOrPlanning(
        FlightTaskType taskType, string? reference, string? notes, string? expectedReference, string? expectedNotes)
    {
        var flight = ScheduleFlight();
        var plannedService = flight.PlannedServices.Single();
        var service = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var creator = Guid.NewGuid();
        var window = TimeWindow.Create(TestData.Now.AddHours(-3), null).Value;

        var task = flight.AddTask(taskType, null, reference, window, notes, creator,
            TestData.Now.ToOffset(TimeSpan.FromHours(3))).Value;

        flight.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(task);
        task.Id.ShouldNotBe(Guid.Empty);
        task.FlightId.ShouldBe(flight.Id);
        task.TaskType.ShouldBe(taskType);
        task.AtaChapter.ShouldBeNull();
        task.CustomerReferenceNumber.ShouldBe(expectedReference);
        task.Notes.ShouldBe(expectedNotes);
        task.Window.ShouldBe(window);
        task.Window.ShouldNotBeSameAs(window);
        task.CreatedByUserId.ShouldBe(creator);
        task.CreatedAtUtc.ShouldBe(TestData.Now);
        task.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        task.EndedByUserId.ShouldBeNull();
        task.UpdatedByUserId.ShouldBeNull();
        task.UpdatedAtUtc.ShouldBeNull();
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.Performers.ShouldBeEmpty();
        task.Attachments.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.PlannedServices.ShouldHaveSingleItem().ShouldBeSameAs(plannedService);
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        flight.Schedule.ShouldBe(TestData.Schedule());
        flight.ArrivalSubmissions.ShouldBeEmpty();
        flight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateTask_PreservesItsIdentityCreatorAndChildren_AndAllowsChangingOrClearingAtaChapter()
    {
        var flight = ScheduleFlight();
        var originalChapter = Chapter();
        var task = flight.AddTask(FlightTaskType.Major, originalChapter, null, Window(), null,
            Guid.NewGuid(), TestData.Now).Value;
        var id = task.Id;
        var creator = task.CreatedByUserId;
        var tool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, creator, TestData.Now.AddMinutes(1)).Value;
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), creator, TestData.Now.AddMinutes(2)).Value;
        var chapter = Chapter();
        var editor = Guid.NewGuid();
        var updatedAt = TestData.Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(3));
        var window = TimeWindow.Create(TestData.Now.AddHours(-1), null).Value;

        var updated = flight.UpdateTask(task.Id, FlightTaskType.Minor, chapter, " NEW-REF ", window,
            " Updated task ", editor, updatedAt).Value;

        updated.ShouldBeSameAs(task);
        updated.Id.ShouldBe(id);
        updated.TaskType.ShouldBe(FlightTaskType.Minor);
        updated.AtaChapter.ShouldBe(chapter);
        updated.AtaChapter.ShouldNotBeSameAs(chapter);
        updated.CustomerReferenceNumber.ShouldBe("NEW-REF");
        updated.Notes.ShouldBe("Updated task");
        updated.Window.ShouldBe(window);
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(TestData.Now);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        updated.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        updated.Tools.ShouldHaveSingleItem().ShouldBeSameAs(tool);
        updated.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        flight.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());

        flight.UpdateTask(task.Id, task.TaskType, null, task.CustomerReferenceNumber, task.Window,
            task.Notes, editor, updatedAt.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        task.AtaChapter.ShouldBeNull();
        task.Tools.ShouldHaveSingleItem().ShouldBeSameAs(tool);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
    }

    [Fact]
    public void TaskEndActor_TracksTheUserRecordingTheEnd_AndOtherEditsPreserveTheCreatorAndCloser()
    {
        var flight = ScheduleFlight();
        var creator = Guid.NewGuid();
        var closer = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var task = flight.AddTask(FlightTaskType.Major, null, null, Window(), null, creator, TestData.Now).Value;
        var closedWindow = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

        flight.UpdateTask(task.Id, task.TaskType, null, null, closedWindow, null, closer,
            TestData.Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        task.CreatedByUserId.ShouldBe(creator);
        task.EndedByUserId.ShouldBe(closer);

        flight.UpdateTask(task.Id, FlightTaskType.Minor, Chapter(), "REF",
            TimeWindow.Create(TestData.Now.AddMinutes(-5), closedWindow.ToUtc).Value, "Notes", editor,
            TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        var tool = flight.AddTaskTool(task.Id, Tool(), Duration(), "Tool note", editor, TestData.Now.AddHours(2)).Value;
        var material = flight.AddTaskMaterial(task.Id, Material(), Quantity(), "Material note", editor, TestData.Now.AddHours(2)).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), "Support note", editor, TestData.Now.AddHours(2)).Value;
        var performer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), "Work note", editor, TestData.Now.AddHours(2)).Value;
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), editor, TestData.Now.AddHours(2)).Value;
        flight.RemoveTaskTool(task.Id, tool.Id, editor, TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskMaterial(task.Id, material.Id, editor, TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskGeneralSupport(task.Id, support.Id, editor, TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskPerformer(task.Id, performer.Id, editor, TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskAttachment(task.Id, attachment.Id, editor, TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();

        task.CreatedByUserId.ShouldBe(creator);
        task.EndedByUserId.ShouldBe(closer);
        task.UpdatedByUserId.ShouldBe(editor);

        flight.UpdateTask(task.Id, task.TaskType, task.AtaChapter, task.CustomerReferenceNumber,
            TimeWindow.Create(task.Window.FromUtc, TestData.Now.AddHours(3)).Value, task.Notes, editor,
            TestData.Now.AddHours(3)).IsSuccess.ShouldBeTrue();
        task.EndedByUserId.ShouldBe(editor);

        flight.UpdateTask(task.Id, task.TaskType, task.AtaChapter, task.CustomerReferenceNumber,
            Window(), task.Notes, Guid.NewGuid(), TestData.Now.AddHours(4)).IsSuccess.ShouldBeTrue();
        task.CreatedByUserId.ShouldBe(creator);
        task.EndedByUserId.ShouldBeNull();
    }

    [Fact]
    public void AddTask_WithAClosedWindow_RecordsTheCreatorAsTheEndActor()
    {
        var flight = ScheduleFlight();
        var creator = Guid.NewGuid();
        var window = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

        var task = flight.AddTask(FlightTaskType.Minor, Chapter(), null, window, null,
            creator, TestData.Now.AddHours(2)).Value;

        task.CreatedByUserId.ShouldBe(creator);
        task.EndedByUserId.ShouldBe(creator);
        task.Window.ShouldBe(window);
    }

    [Fact]
    public void TaskChildren_CaptureCatalogSpecificUsageAndNotes_AndRejectDuplicateSelections()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var tool = Tool();
        var material = Material();
        var support = Support();
        var employee = TestData.Staff();
        var usage = Duration(TestData.Now.AddMinutes(10), null);

        var firstTool = flight.AddTaskTool(task.Id, tool, usage, " Tool note ", user, TestData.Now).Value;
        var firstMaterial = flight.AddTaskMaterial(task.Id, material, Quantity(2.25m), " Material note ", user, TestData.Now).Value;
        var firstSupport = flight.AddTaskGeneralSupport(task.Id, support, Quantity(3), " Support note ", user, TestData.Now).Value;
        var firstPerformer = flight.AddTaskPerformer(task.Id, employee, Window(), " Work note ", user, TestData.Now).Value;
        Result[] duplicates =
        [
            flight.AddTaskTool(task.Id, tool, Duration(), null, user, TestData.Now.AddMinutes(1)),
            flight.AddTaskMaterial(task.Id, material, Quantity(), null, user, TestData.Now.AddMinutes(1)),
            flight.AddTaskGeneralSupport(task.Id, support, Quantity(), null, user, TestData.Now.AddMinutes(1)),
            flight.AddTaskPerformer(task.Id, employee, Window(), null, user, TestData.Now.AddMinutes(1))
        ];

        task.Tools.ShouldHaveSingleItem().ShouldBeSameAs(firstTool);
        task.Materials.ShouldHaveSingleItem().ShouldBeSameAs(firstMaterial);
        task.GeneralSupports.ShouldHaveSingleItem().ShouldBeSameAs(firstSupport);
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(firstPerformer);
        firstTool.Tool.ShouldBe(tool);
        firstTool.Tool.ShouldNotBeSameAs(tool);
        firstMaterial.Material.ShouldBe(material);
        firstMaterial.Material.ShouldNotBeSameAs(material);
        firstSupport.GeneralSupport.ShouldBe(support);
        firstSupport.GeneralSupport.ShouldNotBeSameAs(support);
        firstPerformer.Employee.ShouldBe(employee);
        firstPerformer.Employee.ShouldNotBeSameAs(employee);
        firstTool.Usage.ShouldBe(usage);
        firstTool.Usage.ShouldNotBeSameAs(usage);
        firstTool.Tool.CalculationType.ShouldBe(ResourceCalculationType.Duration);
        firstMaterial.Usage.ShouldBe(Quantity(2.25m));
        firstMaterial.Material.CalculationType.ShouldBe(ResourceCalculationType.Quantity);
        firstSupport.Usage.ShouldBe(Quantity(3));
        firstSupport.GeneralSupport.CalculationType.ShouldBe(ResourceCalculationType.Quantity);
        firstPerformer.Window.ShouldBe(Window());
        firstTool.Notes.ShouldBe("Tool note");
        firstMaterial.Notes.ShouldBe("Material note");
        firstSupport.Notes.ShouldBe("Support note");
        firstPerformer.Notes.ShouldBe("Work note");
        duplicates.Select(result => result.Error.Code).ShouldBe([
            "Operations.ResourceUsage.ToolDuplicate", "Operations.ResourceUsage.MaterialDuplicate",
            "Operations.ResourceUsage.GeneralSupportDuplicate", "Operations.WorkOrder.EmployeeAssignmentsRequired"]);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void ChildUpdatesAndRemovals_AreScopedAndPreserveIdentitiesOtherPeriodsAndAttachments()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var firstTool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var secondTool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var firstMaterial = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var secondMaterial = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var firstSupport = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var secondSupport = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var firstPerformer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var secondPerformer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), user, TestData.Now).Value;
        var tool = Tool();
        var material = Material();
        var support = Support();
        var employee = TestData.Staff();
        var window = TimeWindow.Create(TestData.Now.AddHours(3), TestData.Now.AddHours(4)).Value;
        var editor = Guid.NewGuid();
        var now = TestData.Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(3));

        flight.UpdateTaskTool(task.Id, firstTool.Id, tool, Duration(window.FromUtc, window.ToUtc), " Updated tool ", editor, now).Value.ShouldBeSameAs(firstTool);
        flight.UpdateTaskMaterial(task.Id, firstMaterial.Id, material, Quantity(2), " Updated material ", editor, now).Value.ShouldBeSameAs(firstMaterial);
        flight.UpdateTaskGeneralSupport(task.Id, firstSupport.Id, support, Quantity(3), " Updated support ", editor, now).Value.ShouldBeSameAs(firstSupport);
        flight.UpdateTaskPerformer(task.Id, firstPerformer.Id, employee, window, " Updated work ", editor, now).Value.ShouldBeSameAs(firstPerformer);

        firstTool.FlightTaskId.ShouldBe(task.Id);
        firstMaterial.FlightTaskId.ShouldBe(task.Id);
        firstSupport.FlightTaskId.ShouldBe(task.Id);
        firstPerformer.FlightTaskId.ShouldBe(task.Id);
        firstTool.Tool.ShouldBe(tool);
        firstMaterial.Material.ShouldBe(material);
        firstSupport.GeneralSupport.ShouldBe(support);
        firstPerformer.Employee.ShouldBe(employee);
        firstTool.Usage.ShouldBe(Duration(window.FromUtc, window.ToUtc));
        firstMaterial.Usage.ShouldBe(Quantity(2));
        firstSupport.Usage.ShouldBe(Quantity(3));
        firstPerformer.Window.ShouldBe(window);
        firstTool.Notes.ShouldBe("Updated tool");
        firstMaterial.Notes.ShouldBe("Updated material");
        firstSupport.Notes.ShouldBe("Updated support");
        firstPerformer.Notes.ShouldBe("Updated work");
        task.UpdatedByUserId.ShouldBe(editor);
        task.UpdatedAtUtc.ShouldBe(now.ToUniversalTime());
        flight.UpdatedAtUtc.ShouldBe(now.ToUniversalTime());

        flight.RemoveTaskTool(task.Id, firstTool.Id, editor, now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskMaterial(task.Id, firstMaterial.Id, editor, now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskGeneralSupport(task.Id, firstSupport.Id, editor, now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.RemoveTaskPerformer(task.Id, firstPerformer.Id, editor, now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        task.Tools.ShouldHaveSingleItem().ShouldBeSameAs(secondTool);
        task.Materials.ShouldHaveSingleItem().ShouldBeSameAs(secondMaterial);
        task.GeneralSupports.ShouldHaveSingleItem().ShouldBeSameAs(secondSupport);
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(secondPerformer);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        task.UpdatedAtUtc.ShouldBe(now.AddMinutes(1).ToUniversalTime());
        flight.UpdatedAtUtc.ShouldBe(now.AddMinutes(1).ToUniversalTime());
    }

    [Fact]
    public void EquivalentTaskAndChildUpdates_AreNoOpsForAuditTimesActorsAndGraphIdentities()
    {
        var flight = ScheduleFlight();
        var chapter = Chapter();
        var window = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var creator = Guid.NewGuid();
        var task = flight.AddTask(FlightTaskType.Major, chapter, null, window, null, creator, TestData.Now).Value;
        var tool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, creator, TestData.Now.AddMinutes(1)).Value;
        var material = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, creator, TestData.Now.AddMinutes(1)).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, creator, TestData.Now.AddMinutes(1)).Value;
        var performer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, creator, TestData.Now.AddMinutes(1)).Value;
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), creator, TestData.Now.AddMinutes(1)).Value;
        var previousTime = flight.UpdatedAtUtc;
        var editor = Guid.NewGuid();
        var now = TestData.Now.AddMinutes(2);
        var equivalentWindow = TimeWindow.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), null).Value;

        flight.UpdateTask(task.Id, task.TaskType, new AtaChapterSnapshot(chapter.AtaChapterId, chapter.Code, chapter.Title),
            " ", TimeWindow.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)),
                TestData.Now.AddHours(1).ToOffset(TimeSpan.FromHours(3))).Value, " ", editor, now).Value.ShouldBeSameAs(task);
        flight.UpdateTaskTool(task.Id, tool.Id, new ToolSnapshot(tool.Tool.ToolId, tool.Tool.Name),
            Duration(equivalentWindow.FromUtc, equivalentWindow.ToUtc), " ", editor, now).Value.ShouldBeSameAs(tool);
        flight.UpdateTaskMaterial(task.Id, material.Id, new MaterialSnapshot(material.Material.MaterialId, material.Material.Name),
            Quantity(), " ", editor, now).Value.ShouldBeSameAs(material);
        flight.UpdateTaskGeneralSupport(task.Id, support.Id,
            new GeneralSupportSnapshot(support.GeneralSupport.GeneralSupportId, support.GeneralSupport.Name),
            Quantity(), " ", editor, now).Value.ShouldBeSameAs(support);
        flight.UpdateTaskPerformer(task.Id, performer.Id,
            new StaffMemberSnapshot(performer.Employee.StaffMemberId, performer.Employee.FullName, performer.Employee.EmployeeId),
            equivalentWindow, " ", editor, now).Value.ShouldBeSameAs(performer);

        task.CreatedByUserId.ShouldBe(creator);
        task.EndedByUserId.ShouldBe(creator);
        task.UpdatedByUserId.ShouldBe(creator);
        task.UpdatedAtUtc.ShouldBe(previousTime);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
        task.Tools.ShouldHaveSingleItem().ShouldBeSameAs(tool);
        task.Materials.ShouldHaveSingleItem().ShouldBeSameAs(material);
        task.GeneralSupports.ShouldHaveSingleItem().ShouldBeSameAs(support);
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
    }

    [Fact]
    public void AttachmentRemoval_PreservesUploadMetadataAndOtherTaskChildren()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var details = Attachment();
        var uploadedAt = TestData.Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(3));
        var first = flight.AddTaskAttachment(task.Id, details, user, uploadedAt).Value;
        var second = flight.AddTaskAttachment(task.Id, Attachment("second.jpg"), user, uploadedAt).Value;
        var performer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, uploadedAt).Value;
        var remover = Guid.NewGuid();

        var removed = flight.RemoveTaskAttachment(task.Id, first.Id, remover, uploadedAt.AddMinutes(1)).Value;

        removed.ShouldBeSameAs(first);
        removed.FlightTaskId.ShouldBe(task.Id);
        removed.Details.ShouldBe(details);
        removed.Details.ShouldNotBeSameAs(details);
        removed.UploadedByUserId.ShouldBe(user);
        removed.UploadedAtUtc.ShouldBe(uploadedAt.ToUniversalTime());
        removed.UploadedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(second);
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        task.UpdatedByUserId.ShouldBe(remover);
        task.UpdatedAtUtc.ShouldBe(uploadedAt.AddMinutes(1).ToUniversalTime());
    }

    [Fact]
    public void ForeignTaskAndChildIdentifiers_AreRejectedWithoutChangingEitherTask()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var otherTask = AddTask(flight);
        var foreignTask = AddTask(ScheduleFlight());
        var user = Guid.NewGuid();
        var tool = flight.AddTaskTool(otherTask.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var material = flight.AddTaskMaterial(otherTask.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var support = flight.AddTaskGeneralSupport(otherTask.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var performer = flight.AddTaskPerformer(otherTask.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var attachment = flight.AddTaskAttachment(otherTask.Id, Attachment(), user, TestData.Now).Value;
        var previousTime = flight.UpdatedAtUtc;
        Result[] failures =
        [
            flight.UpdateTask(foreignTask.Id, FlightTaskType.Minor, null, null, Window(), null, user, TestData.Now.AddMinutes(1)),
            flight.AddTaskTool(foreignTask.Id, Tool(), Duration(), null, user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskTool(task.Id, tool.Id, Tool(), Duration(), null, user, TestData.Now.AddMinutes(1)),
            flight.RemoveTaskTool(task.Id, tool.Id, user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskMaterial(task.Id, material.Id, Material(), Quantity(), null, user, TestData.Now.AddMinutes(1)),
            flight.RemoveTaskMaterial(task.Id, material.Id, user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskGeneralSupport(task.Id, support.Id, Support(), Quantity(), null, user, TestData.Now.AddMinutes(1)),
            flight.RemoveTaskGeneralSupport(task.Id, support.Id, user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskPerformer(task.Id, performer.Id, TestData.Staff(), Window(), null, user, TestData.Now.AddMinutes(1)),
            flight.RemoveTaskPerformer(task.Id, performer.Id, user, TestData.Now.AddMinutes(1)),
            flight.RemoveTaskAttachment(task.Id, attachment.Id, user, TestData.Now.AddMinutes(1))
        ];

        failures.Select(result => result.Error.Code).ShouldBe([
            "Operations.FlightTask.NotFound", "Operations.FlightTask.NotFound",
            "Operations.FlightTask.ToolNotFound", "Operations.FlightTask.ToolNotFound",
            "Operations.FlightTask.MaterialNotFound", "Operations.FlightTask.MaterialNotFound",
            "Operations.FlightTask.GeneralSupportNotFound", "Operations.FlightTask.GeneralSupportNotFound",
            "Operations.FlightTask.PerformerNotFound", "Operations.FlightTask.PerformerNotFound",
            "Operations.FlightTask.AttachmentNotFound"]);
        failures.ShouldAllBe(result => result.Error.Type == ErrorType.NotFound);
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.Performers.ShouldBeEmpty();
        task.Attachments.ShouldBeEmpty();
        otherTask.Tools.ShouldHaveSingleItem().ShouldBeSameAs(tool);
        otherTask.Materials.ShouldHaveSingleItem().ShouldBeSameAs(material);
        otherTask.GeneralSupports.ShouldHaveSingleItem().ShouldBeSameAs(support);
        otherTask.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        otherTask.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Theory]
    [InlineData("task-type", "Operations.FlightTask.TaskTypeInvalid")]
    [InlineData("ata-chapter", "Operations.FlightTask.AtaChapterInvalid")]
    [InlineData("window", "Operations.FlightTask.WindowRequired")]
    [InlineData("user", "Operations.FlightTask.UserRequired")]
    public void AddTask_InvalidInput_DoesNotAddWorkOrChangeTheFlight(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();

        var result = flight.AddTask(invalidInput == "task-type" ? (FlightTaskType)99 : FlightTaskType.Major,
            invalidInput == "ata-chapter" ? new AtaChapterSnapshot(Guid.Empty, "00", "General") : null,
            null, invalidInput == "window" ? null : Window(), null,
            invalidInput == "user" ? Guid.Empty : Guid.NewGuid(), TestData.Now);

        result.Error.Code.ShouldBe(errorCode);
        flight.Tasks.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void InvalidTaskAndChildUpdates_DoNotPartiallyChangeTheGraphOrItsActors()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var tool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var material = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var performer = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var previousTime = flight.UpdatedAtUtc;
        var previousTool = tool.Tool;
        var previousMaterial = material.Material;
        var previousSupport = support.GeneralSupport;
        var previousEmployee = performer.Employee;
        Result[] failures =
        [
            flight.UpdateTask(task.Id, (FlightTaskType)99, Chapter(), "NEW", Window(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskTool(task.Id, tool.Id, null, Duration(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskMaterial(task.Id, material.Id, null, Quantity(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskGeneralSupport(task.Id, support.Id, null, Quantity(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskPerformer(task.Id, performer.Id, null, Window(), "NEW", user, TestData.Now.AddMinutes(1))
        ];

        failures.Select(result => result.Error.Code).ShouldBe([
            "Operations.FlightTask.TaskTypeInvalid", "Operations.FlightTask.ToolRequired", "Operations.FlightTask.MaterialRequired",
            "Operations.FlightTask.GeneralSupportRequired", "Operations.FlightTask.EmployeeRequired"]);
        task.TaskType.ShouldBe(FlightTaskType.Major);
        task.AtaChapter.ShouldBeNull();
        task.CustomerReferenceNumber.ShouldBeNull();
        task.Notes.ShouldBeNull();
        task.EndedByUserId.ShouldBeNull();
        tool.Tool.ShouldBeSameAs(previousTool);
        material.Material.ShouldBeSameAs(previousMaterial);
        support.GeneralSupport.ShouldBeSameAs(previousSupport);
        performer.Employee.ShouldBeSameAs(previousEmployee);
        tool.Notes.ShouldBeNull();
        material.Notes.ShouldBeNull();
        support.Notes.ShouldBeNull();
        performer.Notes.ShouldBeNull();
        task.UpdatedByUserId.ShouldBe(user);
        task.UpdatedAtUtc.ShouldBe(previousTime);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Theory]
    [InlineData("tool", "Operations.FlightTask.ToolRequired")]
    [InlineData("tool-id", "Operations.FlightTask.ToolRequired")]
    [InlineData("material", "Operations.FlightTask.MaterialRequired")]
    [InlineData("material-id", "Operations.FlightTask.MaterialRequired")]
    [InlineData("support", "Operations.FlightTask.GeneralSupportRequired")]
    [InlineData("support-id", "Operations.FlightTask.GeneralSupportRequired")]
    [InlineData("employee", "Operations.FlightTask.EmployeeRequired")]
    [InlineData("employee-id", "Operations.FlightTask.EmployeeRequired")]
    [InlineData("tool-window", "Operations.FlightTask.UsageRequired")]
    [InlineData("material-window", "Operations.FlightTask.UsageRequired")]
    [InlineData("support-window", "Operations.FlightTask.UsageRequired")]
    [InlineData("employee-window", "Operations.FlightTask.WindowRequired")]
    [InlineData("attachment", "Operations.FlightTask.AttachmentRequired")]
    public void MissingChildDetails_DoNotAddRecordsOrChangeTheTask(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var now = TestData.Now.AddMinutes(1);
        Result result = invalidInput switch
        {
            "tool" => flight.AddTaskTool(task.Id, null, Duration(), null, user, now),
            "tool-id" => flight.AddTaskTool(task.Id, new ToolSnapshot(Guid.Empty, "Tool"), Duration(), null, user, now),
            "material" => flight.AddTaskMaterial(task.Id, null, Quantity(), null, user, now),
            "material-id" => flight.AddTaskMaterial(task.Id, new MaterialSnapshot(Guid.Empty, "Material"), Quantity(), null, user, now),
            "support" => flight.AddTaskGeneralSupport(task.Id, null, Quantity(), null, user, now),
            "support-id" => flight.AddTaskGeneralSupport(task.Id, new GeneralSupportSnapshot(Guid.Empty, "Support"), Quantity(), null, user, now),
            "employee" => flight.AddTaskPerformer(task.Id, null, Window(), null, user, now),
            "employee-id" => flight.AddTaskPerformer(task.Id, new StaffMemberSnapshot(Guid.Empty, "Staff", "E1"), Window(), null, user, now),
            "tool-window" => flight.AddTaskTool(task.Id, Tool(), null, null, user, now),
            "material-window" => flight.AddTaskMaterial(task.Id, Material(), null, null, user, now),
            "support-window" => flight.AddTaskGeneralSupport(task.Id, Support(), null, null, user, now),
            "employee-window" => flight.AddTaskPerformer(task.Id, TestData.Staff(), null, null, user, now),
            _ => flight.AddTaskAttachment(task.Id, null, user, now)
        };

        result.Error.Code.ShouldBe(errorCode);
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.Performers.ShouldBeEmpty();
        task.Attachments.ShouldBeEmpty();
        task.UpdatedByUserId.ShouldBeNull();
        task.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnyCancellationSubmission_IncludingPendingConflicts_BlocksAllTaskWrites(bool pending)
    {
        var flight = ScheduleFlight();
        var cancellation = CancellationDetails.Create(TestData.Now, "Weather").Value;
        flight.SubmitCancellation(TestData.Staff(), cancellation, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        if (pending)
            flight.SubmitCancellation(TestData.Staff(), cancellation, Guid.NewGuid(), TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var previousTime = flight.UpdatedAtUtc;
        var user = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = TestData.Now.AddMinutes(2);
        Result[] failures =
        [
            flight.AddTask(FlightTaskType.Major, null, null, Window(), null, user, now),
            flight.UpdateTask(taskId, FlightTaskType.Major, null, null, Window(), null, user, now),
            flight.AddTaskTool(taskId, Tool(), Duration(), null, user, now),
            flight.UpdateTaskTool(taskId, childId, Tool(), Duration(), null, user, now),
            flight.RemoveTaskTool(taskId, childId, user, now),
            flight.AddTaskMaterial(taskId, Material(), Quantity(), null, user, now),
            flight.UpdateTaskMaterial(taskId, childId, Material(), Quantity(), null, user, now),
            flight.RemoveTaskMaterial(taskId, childId, user, now),
            flight.AddTaskGeneralSupport(taskId, Support(), Quantity(), null, user, now),
            flight.UpdateTaskGeneralSupport(taskId, childId, Support(), Quantity(), null, user, now),
            flight.RemoveTaskGeneralSupport(taskId, childId, user, now),
            flight.AddTaskPerformer(taskId, TestData.Staff(), Window(), null, user, now),
            flight.UpdateTaskPerformer(taskId, childId, TestData.Staff(), Window(), null, user, now),
            flight.RemoveTaskPerformer(taskId, childId, user, now),
            flight.AddTaskAttachment(taskId, Attachment(), user, now),
            flight.RemoveTaskAttachment(taskId, childId, user, now)
        ];

        failures.ShouldAllBe(result => result.Error.Code == "Operations.Flight.CancellationSubmitted"
            && result.Error.Type == ErrorType.Conflict);
        flight.Tasks.ShouldBeEmpty();
        flight.CancellationSubmissions.Count.ShouldBe(pending ? 2 : 1);
        flight.CancellationSubmissions.ShouldAllBe(item => item.IsAccepted == !pending);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void ExistingTask_BlocksCancellationWithoutRemovingActualWork()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var previousTime = flight.UpdatedAtUtc;

        var result = flight.SubmitCancellation(TestData.Staff(), CancellationDetails.Create(TestData.Now, null).Value,
            Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        flight.CancellationSubmissions.ShouldBeEmpty();
        flight.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(task);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void TaskResources_PreserveNonDefaultCatalogCalculationTypesAcrossCopiesAndEquivalentUpdates()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var toolSnapshot = new ToolSnapshot(Guid.NewGuid(), "Consumable tool", ResourceCalculationType.Quantity);
        var materialSnapshot = new MaterialSnapshot(Guid.NewGuid(), "Timed material", ResourceCalculationType.Duration);
        var supportSnapshot = new GeneralSupportSnapshot(Guid.NewGuid(), "Timed support", ResourceCalculationType.Duration);
        var tool = flight.AddTaskTool(task.Id, toolSnapshot, Quantity(2), null, user, TestData.Now).Value;
        var material = flight.AddTaskMaterial(task.Id, materialSnapshot, Duration(), null, user, TestData.Now).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, supportSnapshot, Duration(), null, user, TestData.Now).Value;

        tool.Tool.ShouldBe(toolSnapshot);
        tool.Tool.ShouldNotBeSameAs(toolSnapshot);
        tool.Usage.CalculationType.ShouldBe(ResourceCalculationType.Quantity);
        material.Material.ShouldBe(materialSnapshot);
        material.Material.ShouldNotBeSameAs(materialSnapshot);
        material.Usage.CalculationType.ShouldBe(ResourceCalculationType.Duration);
        support.GeneralSupport.ShouldBe(supportSnapshot);
        support.GeneralSupport.ShouldNotBeSameAs(supportSnapshot);
        support.Usage.CalculationType.ShouldBe(ResourceCalculationType.Duration);

        flight.UpdateTaskTool(task.Id, tool.Id,
            new ToolSnapshot(toolSnapshot.ToolId, toolSnapshot.Name, ResourceCalculationType.Quantity), Quantity(2), " ",
            Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value.ShouldBeSameAs(tool);
        flight.UpdateTaskMaterial(task.Id, material.Id,
            new MaterialSnapshot(materialSnapshot.MaterialId, materialSnapshot.Name, ResourceCalculationType.Duration), Duration(), " ",
            Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value.ShouldBeSameAs(material);
        flight.UpdateTaskGeneralSupport(task.Id, support.Id,
            new GeneralSupportSnapshot(supportSnapshot.GeneralSupportId, supportSnapshot.Name, ResourceCalculationType.Duration), Duration(), " ",
            Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value.ShouldBeSameAs(support);

        task.UpdatedByUserId.ShouldBe(user);
        task.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData("tool", true, "Operations.ResourceUsage.ToolFromBeforeTask")]
    [InlineData("tool", false, "Operations.ResourceUsage.ToolToAfterTask")]
    [InlineData("material", true, "Operations.ResourceUsage.MaterialFromBeforeTask")]
    [InlineData("material", false, "Operations.ResourceUsage.MaterialToAfterTask")]
    [InlineData("support", true, "Operations.ResourceUsage.GeneralSupportFromBeforeTask")]
    [InlineData("support", false, "Operations.ResourceUsage.GeneralSupportToAfterTask")]
    [InlineData("employee", true, "Operations.WorkOrder.EmployeeFromBeforeLine")]
    [InlineData("employee", false, "Operations.WorkOrder.EmployeeToAfterLine")]
    public void TaskChildWindows_MustFitTheTask_WhenAnEndIsKnown(string kind, bool beforeStart, string code)
    {
        var flight = ScheduleFlight();
        var task = flight.AddTask(FlightTaskType.Major, null, null,
            TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value, null, Guid.NewGuid(), TestData.Now).Value;
        var from = beforeStart ? TestData.Now.AddMinutes(-1) : TestData.Now;
        var to = beforeStart ? TestData.Now.AddMinutes(30) : TestData.Now.AddHours(2);
        var user = Guid.NewGuid();
        var now = TestData.Now.AddMinutes(1);
        Result result = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id, Tool(), Duration(from, to), null, user, now),
            "material" => flight.AddTaskMaterial(task.Id,
                new MaterialSnapshot(Guid.NewGuid(), "Material", ResourceCalculationType.Duration), Duration(from, to), null, user, now),
            "support" => flight.AddTaskGeneralSupport(task.Id,
                new GeneralSupportSnapshot(Guid.NewGuid(), "Support", ResourceCalculationType.Duration), Duration(from, to), null, user, now),
            _ => flight.AddTaskPerformer(task.Id, TestData.Staff(), TimeWindow.Create(from, to).Value, null, user, now)
        };

        result.Error.Code.ShouldBe(code);
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.Performers.ShouldBeEmpty();
        task.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData("tool", true, "Operations.ResourceUsage.ToolFromBeforeTask")]
    [InlineData("tool", false, "Operations.ResourceUsage.ToolToAfterTask")]
    [InlineData("material", true, "Operations.ResourceUsage.MaterialFromBeforeTask")]
    [InlineData("material", false, "Operations.ResourceUsage.MaterialToAfterTask")]
    [InlineData("support", true, "Operations.ResourceUsage.GeneralSupportFromBeforeTask")]
    [InlineData("support", false, "Operations.ResourceUsage.GeneralSupportToAfterTask")]
    [InlineData("employee", true, "Operations.WorkOrder.EmployeeFromBeforeLine")]
    [InlineData("employee", false, "Operations.WorkOrder.EmployeeToAfterLine")]
    public void TaskWindowUpdate_RevalidatesRetainedChildrenBeforeChangingAnyDetails(string kind, bool moveStart, string code)
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var creator = task.CreatedByUserId;
        var from = TestData.Now.AddMinutes(15);
        var to = TestData.Now.AddMinutes(90);
        Result added = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id, Tool(), Duration(from, to), null, creator, TestData.Now),
            "material" => flight.AddTaskMaterial(task.Id,
                new MaterialSnapshot(Guid.NewGuid(), "Material", ResourceCalculationType.Duration), Duration(from, to), null, creator, TestData.Now),
            "support" => flight.AddTaskGeneralSupport(task.Id,
                new GeneralSupportSnapshot(Guid.NewGuid(), "Support", ResourceCalculationType.Duration), Duration(from, to), null, creator, TestData.Now),
            _ => flight.AddTaskPerformer(task.Id, TestData.Staff(), TimeWindow.Create(from, to).Value, null, creator, TestData.Now)
        };
        added.IsSuccess.ShouldBeTrue();
        var originalWindow = task.Window;
        var changedWindow = TimeWindow.Create(moveStart ? TestData.Now.AddMinutes(30) : TestData.Now,
            moveStart ? null : TestData.Now.AddMinutes(60)).Value;

        var result = flight.UpdateTask(task.Id, FlightTaskType.Minor, Chapter(), "NEW", changedWindow, "NEW",
            Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(code);
        task.TaskType.ShouldBe(FlightTaskType.Major);
        task.AtaChapter.ShouldBeNull();
        task.CustomerReferenceNumber.ShouldBeNull();
        task.Notes.ShouldBeNull();
        task.Window.ShouldBeSameAs(originalWindow);
        task.EndedByUserId.ShouldBeNull();
        task.UpdatedByUserId.ShouldBe(creator);
        task.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void TaskChildUpdates_CannotCollideWithAnotherResourceOrEmployeeOnTheSameTask()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var tool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var otherTool = flight.AddTaskTool(task.Id, Tool(), Duration(), null, user, TestData.Now).Value;
        var material = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var otherMaterial = flight.AddTaskMaterial(task.Id, Material(), Quantity(), null, user, TestData.Now).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var otherSupport = flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), null, user, TestData.Now).Value;
        var employee = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var otherEmployee = flight.AddTaskPerformer(task.Id, TestData.Staff(), Window(), null, user, TestData.Now).Value;
        var originalIds = new[] { tool.Tool.ToolId, material.Material.MaterialId, support.GeneralSupport.GeneralSupportId, employee.Employee.StaffMemberId };
        Result[] failures =
        [
            flight.UpdateTaskTool(task.Id, tool.Id, otherTool.Tool, Duration(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskMaterial(task.Id, material.Id, otherMaterial.Material, Quantity(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskGeneralSupport(task.Id, support.Id, otherSupport.GeneralSupport, Quantity(), "NEW", user, TestData.Now.AddMinutes(1)),
            flight.UpdateTaskPerformer(task.Id, employee.Id, otherEmployee.Employee, Window(), "NEW", user, TestData.Now.AddMinutes(1))
        ];

        failures.Select(result => result.Error.Code).ShouldBe([
            "Operations.ResourceUsage.ToolDuplicate", "Operations.ResourceUsage.MaterialDuplicate",
            "Operations.ResourceUsage.GeneralSupportDuplicate", "Operations.WorkOrder.EmployeeAssignmentsRequired"]);
        new[] { tool.Tool.ToolId, material.Material.MaterialId, support.GeneralSupport.GeneralSupportId, employee.Employee.StaffMemberId }
            .ShouldBe(originalIds);
        tool.Notes.ShouldBeNull();
        material.Notes.ShouldBeNull();
        support.Notes.ShouldBeNull();
        employee.Notes.ShouldBeNull();
        task.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void OpenEmployeePeriod_CannotStartAfterAClosedTaskEnds_AndCannotBeStrandedByClosingItsTask()
    {
        var flight = ScheduleFlight();
        var user = Guid.NewGuid();
        var closedWindow = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var employeeWindow = TimeWindow.Create(TestData.Now.AddHours(2), null).Value;
        var closed = flight.AddTask(FlightTaskType.Major, null, null, closedWindow, null, user, TestData.Now).Value;

        var added = flight.AddTaskPerformer(closed.Id, TestData.Staff(), employeeWindow, null, user, TestData.Now.AddMinutes(1));

        added.Error.Code.ShouldBe("Operations.WorkOrder.EmployeeFromAfterLine");
        closed.Performers.ShouldBeEmpty();
        closed.UpdatedAtUtc.ShouldBeNull();

        var open = AddTask(flight);
        var originalWindow = open.Window;
        var performer = flight.AddTaskPerformer(open.Id, TestData.Staff(), employeeWindow, null, user, TestData.Now).Value;
        var updated = flight.UpdateTask(open.Id, FlightTaskType.Minor, Chapter(), "NEW", closedWindow, "NEW",
            Guid.NewGuid(), TestData.Now.AddMinutes(1));

        updated.Error.Code.ShouldBe("Operations.WorkOrder.EmployeeFromAfterLine");
        open.TaskType.ShouldBe(FlightTaskType.Major);
        open.AtaChapter.ShouldBeNull();
        open.Window.ShouldBeSameAs(originalWindow);
        open.CustomerReferenceNumber.ShouldBeNull();
        open.Notes.ShouldBeNull();
        open.EndedByUserId.ShouldBeNull();
        open.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        open.UpdatedByUserId.ShouldBe(user);
        open.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData("tool", "Operations.ResourceUsage.ToolCalculationTypeMismatch")]
    [InlineData("material", "Operations.ResourceUsage.MaterialCalculationTypeMismatch")]
    [InlineData("support", "Operations.ResourceUsage.GeneralSupportCalculationTypeMismatch")]
    public void TaskResourceUsage_MustMatchTheCapturedCatalogCalculationType(string kind, string code)
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        Result result = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id, Tool(), Quantity(), null, user, TestData.Now.AddMinutes(1)),
            "material" => flight.AddTaskMaterial(task.Id, Material(), Duration(), null, user, TestData.Now.AddMinutes(1)),
            _ => flight.AddTaskGeneralSupport(task.Id, Support(), Duration(), null, user, TestData.Now.AddMinutes(1))
        };

        result.Error.Code.ShouldBe(code);
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);

        Result added = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id, Tool(), Duration(), "Original", user, TestData.Now),
            "material" => flight.AddTaskMaterial(task.Id, Material(), Quantity(), "Original", user, TestData.Now),
            _ => flight.AddTaskGeneralSupport(task.Id, Support(), Quantity(), "Original", user, TestData.Now)
        };
        added.IsSuccess.ShouldBeTrue();
        Result updated = kind switch
        {
            "tool" => flight.UpdateTaskTool(task.Id, task.Tools[0].Id, task.Tools[0].Tool, Quantity(), "Changed", user, TestData.Now.AddMinutes(1)),
            "material" => flight.UpdateTaskMaterial(task.Id, task.Materials[0].Id, task.Materials[0].Material, Duration(), "Changed", user, TestData.Now.AddMinutes(1)),
            _ => flight.UpdateTaskGeneralSupport(task.Id, task.GeneralSupports[0].Id, task.GeneralSupports[0].GeneralSupport,
                Duration(), "Changed", user, TestData.Now.AddMinutes(1))
        };

        updated.Error.Code.ShouldBe(code);
        task.Tools.Select(item => item.Notes).Concat(task.Materials.Select(item => item.Notes))
            .Concat(task.GeneralSupports.Select(item => item.Notes)).ShouldHaveSingleItem().ShouldBe("Original");
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData("tool", false, "Operations.WorkOrder.UnknownDescriptionRequired")]
    [InlineData("material", false, "Operations.WorkOrder.UnknownDescriptionRequired")]
    [InlineData("support", false, "Operations.WorkOrder.UnknownDescriptionRequired")]
    [InlineData("tool", true, "Operations.WorkOrder.DescriptionTooLong")]
    [InlineData("material", true, "Operations.WorkOrder.DescriptionTooLong")]
    [InlineData("support", true, "Operations.WorkOrder.DescriptionTooLong")]
    public void TaskResources_PreserveUnknownDescriptionAndDescriptionLengthRules(string kind, bool tooLong, string code)
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        var notes = tooLong ? new string('N', 2001) : " \t ";
        Result result = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id,
                new ToolSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownTool, "Tool"), Duration(), notes, user, TestData.Now.AddMinutes(1)),
            "material" => flight.AddTaskMaterial(task.Id,
                new MaterialSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownMaterial, "Material"), Quantity(), notes, user, TestData.Now.AddMinutes(1)),
            _ => flight.AddTaskGeneralSupport(task.Id,
                new GeneralSupportSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownGeneralSupport, "Support"), Quantity(), notes, user, TestData.Now.AddMinutes(1))
        };

        result.Error.Code.ShouldBe(code);
        task.Tools.ShouldBeEmpty();
        task.Materials.ShouldBeEmpty();
        task.GeneralSupports.ShouldBeEmpty();
        task.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);

        Result added = kind switch
        {
            "tool" => flight.AddTaskTool(task.Id,
                new ToolSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownTool, "Tool"), Duration(), "Original", user, TestData.Now),
            "material" => flight.AddTaskMaterial(task.Id,
                new MaterialSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownMaterial, "Material"), Quantity(), "Original", user, TestData.Now),
            _ => flight.AddTaskGeneralSupport(task.Id,
                new GeneralSupportSnapshot(tooLong ? Guid.NewGuid() : WellKnownMasterDataIds.UnknownGeneralSupport, "Support"), Quantity(), "Original", user, TestData.Now)
        };
        added.IsSuccess.ShouldBeTrue();
        Result updated = kind switch
        {
            "tool" => flight.UpdateTaskTool(task.Id, task.Tools[0].Id, task.Tools[0].Tool, task.Tools[0].Usage,
                notes, user, TestData.Now.AddMinutes(1)),
            "material" => flight.UpdateTaskMaterial(task.Id, task.Materials[0].Id, task.Materials[0].Material, task.Materials[0].Usage,
                notes, user, TestData.Now.AddMinutes(1)),
            _ => flight.UpdateTaskGeneralSupport(task.Id, task.GeneralSupports[0].Id, task.GeneralSupports[0].GeneralSupport,
                task.GeneralSupports[0].Usage, notes, user, TestData.Now.AddMinutes(1))
        };

        updated.Error.Code.ShouldBe(code);
        task.Tools.Select(item => item.Notes).Concat(task.Materials.Select(item => item.Notes))
            .Concat(task.GeneralSupports.Select(item => item.Notes)).ShouldHaveSingleItem().ShouldBe("Original");
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void TaskAndUnknownResources_AllowTrimmedReferenceAndDescriptionLengthBoundaries()
    {
        var flight = ScheduleFlight();
        var user = Guid.NewGuid();
        var reference = new string('R', 200);
        var notes = new string('N', 2000);
        var task = flight.AddTask(FlightTaskType.Major, null, $" {reference} ", Window(), $" {notes} ", user, TestData.Now).Value;

        var tool = flight.AddTaskTool(task.Id, new ToolSnapshot(WellKnownMasterDataIds.UnknownTool, "Unknown Tool"),
            Duration(), $" {notes} ", user, TestData.Now).Value;
        var material = flight.AddTaskMaterial(task.Id, new MaterialSnapshot(WellKnownMasterDataIds.UnknownMaterial, "Unknown Material"),
            Quantity(), $" {notes} ", user, TestData.Now).Value;
        var support = flight.AddTaskGeneralSupport(task.Id, new GeneralSupportSnapshot(WellKnownMasterDataIds.UnknownGeneralSupport, "Unknown Support"),
            Quantity(), $" {notes} ", user, TestData.Now).Value;

        task.CustomerReferenceNumber.ShouldBe(reference);
        task.Notes.ShouldBe(notes);
        tool.Notes.ShouldBe(notes);
        material.Notes.ShouldBe(notes);
        support.Notes.ShouldBe(notes);
    }

    [Theory]
    [InlineData(true, "Operations.WorkOrder.CustomerReferenceNumberTooLong")]
    [InlineData(false, "Operations.WorkOrder.DescriptionTooLong")]
    public void TaskAddAndUpdate_RejectOversizedReferencesAndDescriptionsAtomically(bool referenceTooLong, string code)
    {
        var flight = ScheduleFlight();
        var reference = referenceTooLong ? new string('R', 201) : null;
        var notes = referenceTooLong ? null : new string('N', 2001);
        var user = Guid.NewGuid();

        flight.AddTask(FlightTaskType.Major, null, reference, Window(), notes, user, TestData.Now).Error.Code.ShouldBe(code);
        flight.Tasks.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBeNull();

        var task = AddTask(flight);
        flight.UpdateTask(task.Id, FlightTaskType.Minor, Chapter(), reference, Window(), notes, user,
            TestData.Now.AddMinutes(1)).Error.Code.ShouldBe(code);

        task.TaskType.ShouldBe(FlightTaskType.Major);
        task.AtaChapter.ShouldBeNull();
        task.CustomerReferenceNumber.ShouldBeNull();
        task.Notes.ShouldBeNull();
        task.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void TaskAttachmentLimit_AllowsTenAndReleasesCapacityAfterRemoval_WhilePreservingKind()
    {
        var flight = ScheduleFlight();
        var task = AddTask(flight);
        var user = Guid.NewGuid();
        for (var index = 0; index < 10; index++)
            flight.AddTaskAttachment(task.Id, Attachment($"evidence-{index}.jpg"), user, TestData.Now).IsSuccess.ShouldBeTrue();
        var first = task.Attachments[0];

        var extra = flight.AddTaskAttachment(task.Id, Attachment("extra.jpg"), Guid.NewGuid(), TestData.Now.AddMinutes(1));

        extra.Error.Code.ShouldBe("Operations.WorkOrder.AttachmentLimitExceeded");
        task.Attachments.Count.ShouldBe(10);
        task.UpdatedByUserId.ShouldBe(user);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);

        flight.RemoveTaskAttachment(task.Id, first.Id, user, TestData.Now).IsSuccess.ShouldBeTrue();
        var voice = AttachmentDetails.Create("voice.m4a", "operations/voice.m4a", "audio/mp4", 200, AttachmentKind.Voice).Value;
        var replacement = flight.AddTaskAttachment(task.Id, voice, user, TestData.Now).Value;
        task.Attachments.Count.ShouldBe(10);
        task.Attachments.ShouldAllBe(item => item.Id != first.Id);
        replacement.Details.Kind.ShouldBe(AttachmentKind.Voice);
    }

    private static Flight ScheduleFlight() =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static FlightTask AddTask(Flight flight) =>
        flight.AddTask(FlightTaskType.Major, null, null, Window(), null, Guid.NewGuid(), TestData.Now).Value;

    private static AtaChapterSnapshot Chapter() => new(Guid.NewGuid(), "32", "Landing gear");
    private static ToolSnapshot Tool() => new(Guid.NewGuid(), "Torque wrench");
    private static MaterialSnapshot Material() => new(Guid.NewGuid(), "Hydraulic fluid");
    private static GeneralSupportSnapshot Support() => new(Guid.NewGuid(), "Ground power");
    private static TimeWindow Window() => TimeWindow.Create(TestData.Now, null).Value;
    private static ResourceUsage Duration(DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        ResourceUsage.Create(ResourceCalculationType.Duration, null, from ?? TestData.Now, to).Value;
    private static ResourceUsage Quantity(decimal amount = 1) =>
        ResourceUsage.Create(ResourceCalculationType.Quantity, amount, null, null).Value;
    private static AttachmentDetails Attachment(string fileName = "evidence.jpg") =>
        AttachmentDetails.Create(fileName, $"operations/{fileName}", "image/jpeg", 200, AttachmentKind.Image).Value;
}
