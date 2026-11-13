using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightTaskIdentityTests
{
    [Fact]
    public void Atomic_correction_preserves_task_children_and_attachments_and_invalid_replacement_keeps_existing_state()
    {
        var flight = Schedule(); var actor = Guid.NewGuid(); var id = Guid.NewGuid();
        var p = new FlightTaskPerformerInput(TestData.Staff(), Period(), "Staff notes", Guid.NewGuid());
        var t = new FlightTaskToolInput(new(Guid.NewGuid(), "Tool"), Duration(), "Tool notes", Guid.NewGuid());
        var m = new FlightTaskMaterialInput(new(Guid.NewGuid(), "Material"), Quantity(), "Material notes", Guid.NewGuid());
        var g = new FlightTaskGeneralSupportInput(new(Guid.NewGuid(), "Support"), Quantity(), null, Guid.NewGuid());
        var task = flight.SaveTask(id, FlightTaskType.Major, null, "Ref", Period(), "Task", [p], [t], [m], [g], actor, TestData.Now).Value;
        var fileId = Guid.NewGuid();
        var attachment = flight.AddTaskAttachment(id, AttachmentDetails.Create("photo.png", "files/photo", "image/png", 42, AttachmentKind.Image).Value,
            actor, TestData.Now, id: fileId).Value;
        var performer = task.Performers.Single(); var tool = task.Tools.Single(); var material = task.Materials.Single(); var support = task.GeneralSupports.Single();
        var result = flight.SaveTask(id, FlightTaskType.Minor, null, "Ref2", Period(), "Updated", [p with { Notes = "Corrected staff" }],
            [t], [m with { Usage = ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Quantity, 2.5m, null, null).Value }], [g], actor, TestData.Now.AddMinutes(1));
        result.Value.ShouldBeSameAs(task); task.Id.ShouldBe(id); task.Performers.Single().ShouldBeSameAs(performer);
        task.Tools.Single().ShouldBeSameAs(tool); task.Materials.Single().ShouldBeSameAs(material); task.GeneralSupports.Single().ShouldBeSameAs(support);
        task.Attachments.Single().ShouldBeSameAs(attachment); attachment.Id.ShouldBe(fileId);
        material.Usage.Quantity.ShouldBe(2.5m); performer.Notes.ShouldBe("Corrected staff");
        var before = task.UpdatedAtUtc;
        var rejected = flight.SaveTask(id, FlightTaskType.Major, null, "Invalid", Period(), "Changed", [p],
            [t with { Usage = ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, TestData.Now.AddHours(-3), null).Value }], [], [], actor, TestData.Now.AddMinutes(2));
        rejected.IsFailure.ShouldBeTrue(); task.CustomerReferenceNumber.ShouldBe("REF2"); task.TaskType.ShouldBe(FlightTaskType.Minor);
        task.UpdatedAtUtc.ShouldBe(before); task.Materials.Single().ShouldBeSameAs(material); task.Attachments.Single().ShouldBeSameAs(attachment);
    }

    [Fact]
    public void Duplicate_or_empty_stable_ids_reject_without_creating_another_task()
    {
        var flight = Schedule(); var actor = Guid.NewGuid(); var p = new FlightTaskPerformerInput(TestData.Staff(), Period(), Id: Guid.NewGuid());
        var id = Guid.NewGuid();
        flight.SaveTask(id, FlightTaskType.Major, null, null, Period(), null, [p], [], [], [], actor, TestData.Now).IsSuccess.ShouldBeTrue();
        flight.SaveTask(Guid.NewGuid(), FlightTaskType.Major, null, null, Period(), null, [p], [], [], [], actor, TestData.Now)
            .Error.Code.ShouldBe("Operations.FlightTask.PerformerIdConflict");
        flight.SaveTask(Guid.Empty, FlightTaskType.Major, null, null, Period(), null, [], [], [], [], actor, TestData.Now)
            .Error.Code.ShouldBe("Operations.FlightTask.IdRequired");
        flight.SaveTask(Guid.NewGuid(), FlightTaskType.Major, null, null, Period(), null, [p with { Id = Guid.Empty }], [], [], [], actor, TestData.Now)
            .Error.Code.ShouldBe("Operations.FlightTask.PerformerIdRequired");
        flight.Tasks.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    private static Flight Schedule() => Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
        TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;
    private static TimeWindow Period() => TimeWindow.Create(TestData.Now.AddHours(-1), null).Value;
    private static ResourceUsage Duration() => ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, TestData.Now, null).Value;
    private static ResourceUsage Quantity() => ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Quantity, 1, null, null).Value;
}
