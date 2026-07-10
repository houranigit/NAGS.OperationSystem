using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class WorkOrderTests
{
    private static Flight ScheduleFlight()
    {
        var result = Flight.ScheduleNew(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), aircraftType: null, [TestData.Service()], [TestData.Staff()],
            contractId: null, contractNumber: null, createdByUserId: Guid.NewGuid(), now: TestData.Now);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public void SubmitNew_CopiesFlightContext_AndDefaultsActualFlightNumber()
    {
        var flight = ScheduleFlight();
        var workOrder = SubmitCompletion(flight);

        workOrder.Status.ShouldBe(WorkOrderStatus.Submitted);
        workOrder.Customer.ShouldBe(flight.Customer);
        workOrder.Station.ShouldBe(flight.Station);
        workOrder.OperationType.ShouldBe(flight.OperationType);
        workOrder.PlannedFlightNumber.Value.ShouldBe(flight.FlightNumber.Value);
        workOrder.ActualFlightNumber.Value.ShouldBe(flight.FlightNumber.Value);

        flight.ChangeFlightNumber(TestData.FlightNo("SV999"), TestData.Now.AddMinutes(1));
        workOrder.PlannedFlightNumber.Value.ShouldBe("SV1020");
    }

    [Fact]
    public void SubmitNew_CancellationRequiresCancellationDetails()
    {
        var result = WorkOrder.SubmitNew(
            ScheduleFlight(), WorkOrderType.Cancellation, Guid.NewGuid(), TestData.Staff(), null, null,
            null, null, cancellation: null, null, [], [], TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.CancellationRequired");
    }

    [Fact]
    public void SubmitNew_RejectsPerLandingServiceLine()
    {
        var line = new WorkOrderServiceLineInput(
            TestData.PerLandingService(), TestData.Staff(),
            TimeWindow.Create(TestData.Now, TestData.Now.AddMinutes(30)).Value, null);

        var result = WorkOrder.SubmitNew(
            ScheduleFlight(), WorkOrderType.Completion, Guid.NewGuid(), TestData.Staff(), null, null,
            null, null, null, null, [line], [], TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.PerLandingLineNotAllowed");
    }

    [Fact]
    public void ReconcileTasks_UpdatesExisting_AddsNew_RemovesMissing_AndRejectsForeignIds()
    {
        var workOrder = SubmitCompletion(ScheduleFlight(), tasks: [TaskInput(null, "Initial")]);
        var originalTaskId = workOrder.Tasks[0].Id;

        var update = workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            null,
            null,
            null,
            null,
            null,
            [],
            [TaskInput(originalTaskId, "Updated"), TaskInput(null, "Added")],
            TestData.Now.AddMinutes(1));

        update.IsSuccess.ShouldBeTrue();
        workOrder.Tasks.Count.ShouldBe(2);
        workOrder.Tasks.ShouldContain(t => t.Id == originalTaskId && t.Description == "Updated");
        workOrder.Tasks.ShouldContain(t => t.Id != originalTaskId && t.Description == "Added");

        var foreign = workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            null,
            null,
            null,
            null,
            null,
            [],
            [TaskInput(Guid.NewGuid(), "Foreign")],
            TestData.Now.AddMinutes(2));

        foreign.IsFailure.ShouldBeTrue();
        foreign.Error.Code.ShouldBe("Operations.WorkOrder.TaskIdForeign");
    }

    [Fact]
    public void Approve_CompletionRequiresActualsAndAircraftType()
    {
        var workOrder = SubmitCompletion(ScheduleFlight());

        var result = workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.CompletionApprovalIncomplete");
    }

    [Fact]
    public void Return_ReleasesApprovalNumber_AndMakesWorkOrderEditable()
    {
        var workOrder = SubmitCompletion(ScheduleFlight());
        var actuals = ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var update = workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            TestData.AircraftType(),
            "hz-abc",
            actuals,
            null,
            null,
            [],
            [],
            TestData.Now);
        update.IsSuccess.ShouldBeTrue();
        workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();

        var returned = workOrder.Return(Guid.NewGuid(), "Need correction", TestData.Now.AddMinutes(1));

        returned.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Returned);
        workOrder.IsEditable.ShouldBeTrue();
        workOrder.ApprovalSequence.ShouldBeNull();
        workOrder.ApprovalNumber.ShouldBeNull();
        workOrder.AircraftTailNumber.ShouldBe("HZ-ABC");
    }

    [Fact]
    public void ApprovedWorkOrder_IsLockedForUpdates()
    {
        var workOrder = SubmitApprovedCompletion();

        var update = workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            TestData.AircraftType(),
            null,
            workOrder.Actuals,
            null,
            "Locked edit",
            [],
            [],
            TestData.Now);

        update.IsFailure.ShouldBeTrue();
        update.Error.Code.ShouldBe("Operations.WorkOrder.Locked");
    }

    [Fact]
    public void TaskAttachments_CanBeAddedAndRemovedWhileEditable_AndAreLockedAfterApproval()
    {
        var workOrder = SubmitCompletion(ScheduleFlight(), tasks: [TaskInput(null, "Attach files")]);
        var taskId = workOrder.Tasks[0].Id;

        var added = workOrder.AddTaskAttachment(
            taskId,
            TaskAttachmentKind.Document,
            "work-order-attachments/file.pdf",
            "signed.pdf",
            "application/pdf",
            128,
            TestData.Now.AddMinutes(1));

        added.IsSuccess.ShouldBeTrue();
        workOrder.Tasks[0].Attachments.Count.ShouldBe(1);
        workOrder.Tasks[0].Attachments[0].OriginalFileName.ShouldBe("signed.pdf");

        var removed = workOrder.RemoveTaskAttachment(taskId, added.Value.Id, TestData.Now.AddMinutes(2));
        removed.IsSuccess.ShouldBeTrue();
        removed.Value.ShouldBe("work-order-attachments/file.pdf");
        workOrder.Tasks[0].Attachments.Count.ShouldBe(0);

        workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            TestData.AircraftType(),
            null,
            ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value,
            null,
            null,
            [],
            [TaskInput(taskId, "Attach files")],
            TestData.Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), TestData.Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();

        var locked = workOrder.AddTaskAttachment(
            taskId,
            TaskAttachmentKind.Document,
            "work-order-attachments/file-2.pdf",
            "locked.pdf",
            "application/pdf",
            128,
            TestData.Now.AddMinutes(5));

        locked.IsFailure.ShouldBeTrue();
        locked.Error.Code.ShouldBe("Operations.WorkOrder.Locked");
    }

    [Fact]
    public void CustomerSignature_CanBeSetRemoved_AndIsLockedAfterApproval()
    {
        var workOrder = SubmitCompletion(ScheduleFlight());

        var signed = workOrder.SetCustomerSignature(
            "work-order-signatures/signature.png",
            "customer signature.png",
            "image/png",
            512,
            TestData.Now.AddMinutes(1));

        signed.IsSuccess.ShouldBeTrue();
        workOrder.CustomerSignatureReference.ShouldBe("work-order-signatures/signature.png");
        workOrder.CustomerSignatureFileName.ShouldBe("customer signature.png");
        workOrder.CustomerSignedAtUtc.ShouldBe(TestData.Now.AddMinutes(1));

        var removed = workOrder.RemoveCustomerSignature(TestData.Now.AddMinutes(2));
        removed.IsSuccess.ShouldBeTrue();
        removed.Value.ShouldBe("work-order-signatures/signature.png");
        workOrder.CustomerSignatureReference.ShouldBeNull();

        workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            TestData.AircraftType(),
            null,
            ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value,
            null,
            null,
            [],
            [],
            TestData.Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), TestData.Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();

        var locked = workOrder.SetCustomerSignature(
            "work-order-signatures/locked.png",
            "locked.png",
            "image/png",
            512,
            TestData.Now.AddMinutes(5));

        locked.IsFailure.ShouldBeTrue();
        locked.Error.Code.ShouldBe("Operations.WorkOrder.Locked");
    }

    [Fact]
    public void UpdateDetails_ClearsCustomerSignature_WhenConvertedToCancellation()
    {
        var workOrder = SubmitCompletion(ScheduleFlight());
        workOrder.SetCustomerSignature(
            "work-order-signatures/signature.png",
            "signature.png",
            "image/png",
            512,
            TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        var converted = workOrder.UpdateDetails(
            WorkOrderType.Cancellation,
            workOrder.ActualFlightNumber,
            null,
            null,
            null,
            CancellationDetails.Create(TestData.Now.AddMinutes(2), "Customer canceled").Value,
            null,
            [],
            [],
            TestData.Now.AddMinutes(2));

        converted.IsSuccess.ShouldBeTrue();
        workOrder.CustomerSignatureReference.ShouldBeNull();
        workOrder.CustomerSignedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void SubmitMerged_FlagsGeneratedWorkOrder_AndSourcesCanBeMarkedMerged()
    {
        var flight = ScheduleFlight();
        var first = SubmitCompletion(flight);
        var second = SubmitCompletion(flight);

        var generated = WorkOrder.SubmitMerged(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            TestData.Staff(),
            first.ActualFlightNumber,
            TestData.AircraftType(),
            "hz-merged",
            ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value,
            null,
            "Merged from source work orders",
            [],
            [],
            TestData.Now.AddMinutes(1));

        generated.IsSuccess.ShouldBeTrue();
        generated.Value.IsMergeGenerated.ShouldBeTrue();
        generated.Value.Status.ShouldBe(WorkOrderStatus.Submitted);
        generated.Value.MergedIntoWorkOrderId.ShouldBeNull();
        generated.Value.AircraftTailNumber.ShouldBe("HZ-MERGED");

        first.MarkMergedInto(generated.Value.Id, TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        second.MarkMergedInto(generated.Value.Id, TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        first.Status.ShouldBe(WorkOrderStatus.Merged);
        second.Status.ShouldBe(WorkOrderStatus.Merged);
        first.MergedIntoWorkOrderId.ShouldBe(generated.Value.Id);
        second.MergedIntoWorkOrderId.ShouldBe(generated.Value.Id);
    }

    [Fact]
    public void MarkMergedInto_RejectsApprovedWorkOrder()
    {
        var workOrder = SubmitApprovedCompletion();

        var result = workOrder.MarkMergedInto(Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.Locked");
    }

    [Fact]
    public void Flight_WorkOrderTransitions_MoveThroughInProgressSettledAndReopened()
    {
        var flight = ScheduleFlight();

        flight.OnWorkOrderSubmitted(TestData.Now).IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.InProgress);

        flight.SettleCompleted(TestData.Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.Completed);

        flight.ReopenToInProgress(TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.InProgress);

        flight.SettleCanceled(TestData.Now.AddHours(3)).IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.Canceled);
    }

    [Fact]
    public void Flight_ScheduleNew_AllowsEmptyPlannedServicesOnlyWhenExplicitlyAllowed()
    {
        var blocked = Flight.ScheduleNew(
            TestData.Customer(),
            TestData.Station(),
            TestData.OperationType(),
            TestData.FlightNo(),
            TestData.Schedule(),
            aircraftType: null,
            plannedServices: [],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: TestData.Now);

        blocked.IsFailure.ShouldBeTrue();
        blocked.Error.Code.ShouldBe("Operations.PlannedServices.Required");

        var allowed = Flight.ScheduleNew(
            TestData.Customer(),
            TestData.Station(),
            TestData.OperationType(),
            TestData.FlightNo(),
            TestData.Schedule(),
            aircraftType: null,
            plannedServices: [],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: TestData.Now,
            allowEmptyPlannedServices: true);

        allowed.IsSuccess.ShouldBeTrue();
    }

    private static WorkOrder SubmitCompletion(Flight flight, IReadOnlyList<WorkOrderTaskInput>? tasks = null)
    {
        var result = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            TestData.Staff(),
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            tasks ?? [],
            TestData.Now);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static WorkOrder SubmitApprovedCompletion()
    {
        var workOrder = SubmitCompletion(ScheduleFlight());
        workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            TestData.AircraftType(),
            null,
            ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value,
            null,
            null,
            [],
            [],
            TestData.Now).IsSuccess.ShouldBeTrue();
        workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        return workOrder;
    }

    private static WorkOrderTaskInput TaskInput(Guid? id, string description) =>
        new(
            id,
            TaskType.Major,
            description,
            TimeWindow.Create(TestData.Now, TestData.Now.AddMinutes(30)).Value,
            [TestData.Staff(), TestData.Staff()],
            [new WorkOrderTaskToolInput(TestData.Tool(), Quantity.Create(1).Value)],
            [new WorkOrderTaskMaterialInput(TestData.Material(), Quantity.Create(2).Value)],
            [new WorkOrderTaskGeneralSupportInput(TestData.GeneralSupport(), Quantity.Create(1).Value)]);
}
