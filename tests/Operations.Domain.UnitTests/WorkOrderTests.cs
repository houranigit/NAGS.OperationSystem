using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class WorkOrderTests
{
    private static FlightContext Context(Guid? flightId = null) =>
        new(flightId ?? Guid.NewGuid(), TestData.Customer(), TestData.Station(), TestData.OperationType(),
            TestData.FlightNo(), TestData.Schedule(), null);

    private static ServiceLineInput Line(ServiceSnapshot service) =>
        new(service, ServiceLineOrigin.Planned, TestData.Now, TestData.Now.AddMinutes(30), null, false, [TestData.Staff()]);

    private static AircraftTypeSnapshot Aircraft() => new(Guid.NewGuid(), "Airbus", "A320");

    [Fact]
    public void OpenCompletion_StartsDraft_WithOwner()
    {
        var owner = TestData.Staff();
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), owner, TestData.Now);

        workOrder.Status.ShouldBe(WorkOrderStatus.Draft);
        workOrder.Type.ShouldBe(WorkOrderType.Completion);
        workOrder.OwnerStaffMemberId.ShouldBe(owner.StaffMemberId);
        workOrder.IsOwnedBy(owner.StaffMemberId).ShouldBeTrue();
        workOrder.IsOwnedBy(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void Submit_Completion_WithoutActualServices_Succeeds()
    {
        // Actual services are optional; billing later compares planned vs actual.
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);

        var submit = workOrder.Submit(TestData.Now);

        submit.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Submitted);
    }

    [Fact]
    public void Submit_NotEditable_Fails()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);

        var second = workOrder.Submit(TestData.Now);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("Operations.WorkOrder.NotSubmittable");
    }

    [Fact]
    public void Approve_Completion_RequiresActuals()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.SetActualAircraftType(Aircraft(), TestData.Now);
        workOrder.Submit(TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 1), Guid.NewGuid(), TestData.Now);

        approve.IsFailure.ShouldBeTrue();
        approve.Error.Code.ShouldBe("Operations.WorkOrder.ActualsRequired");
    }

    [Fact]
    public void Approve_Completion_RequiresActualAircraftType()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.SetActualAircraftType(null, TestData.Now);
        workOrder.SetActualTimes(ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value, TestData.Now);
        workOrder.Submit(TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 1), Guid.NewGuid(), TestData.Now);

        approve.IsFailure.ShouldBeTrue();
        approve.Error.Code.ShouldBe("Operations.WorkOrder.ActualAircraftTypeRequired");
    }

    [Fact]
    public void Approve_Completion_WithRequiredFields_Succeeds()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.SetActualAircraftType(Aircraft(), TestData.Now);
        workOrder.SetActualFlightNumber(TestData.FlightNo("SV321"), TestData.Now);
        workOrder.SetActualTimes(ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value, TestData.Now);
        workOrder.Submit(TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 1), Guid.NewGuid(), TestData.Now);

        approve.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Approved);
        workOrder.Number!.Value.ShouldBe("RUH-0001");
        workOrder.FlightNumber.Value.ShouldBe("SV321");
    }

    [Fact]
    public void SetActualFields_AfterSubmit_Fails()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);

        workOrder.SetActualFlightNumber(TestData.FlightNo("SV1"), TestData.Now).IsFailure.ShouldBeTrue();
        workOrder.SetActualAircraftType(Aircraft(), TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Cancellation_Approve_RequiresOnlyCancellationDetails()
    {
        // No actuals, aircraft type, services, tasks, or signature are required for a cancellation.
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, "Customer canceled");
        var workOrder = WorkOrder.OpenCancellation(Context(), cancellation, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 2), Guid.NewGuid(), TestData.Now);

        approve.IsSuccess.ShouldBeTrue();
        workOrder.IsCancellation.ShouldBeTrue();
    }

    [Fact]
    public void ReturnToReview_UnlocksApprovedWorkOrder_AndWipesNumber()
    {
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, null);
        var workOrder = WorkOrder.OpenCancellation(Context(), cancellation, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);
        workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 3), Guid.NewGuid(), TestData.Now);

        var ret = workOrder.ReturnToReview(TestData.Now);

        ret.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Returned);
        workOrder.Number.ShouldBeNull();
        workOrder.ApprovedByUserId.ShouldBeNull();
        workOrder.ApprovedAtUtc.ShouldBeNull();
        workOrder.IsEditable.ShouldBeTrue();
    }

    [Fact]
    public void ReApproval_AfterReturn_GetsNewNumber()
    {
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, null);
        var workOrder = WorkOrder.OpenCancellation(Context(), cancellation, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);
        workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 4), Guid.NewGuid(), TestData.Now);
        workOrder.ReturnToReview(TestData.Now);
        workOrder.Submit(TestData.Now);

        var reapprove = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 5), Guid.NewGuid(), TestData.Now);

        reapprove.IsSuccess.ShouldBeTrue();
        workOrder.Number!.Value.ShouldBe("RUH-0005");
    }

    [Fact]
    public void ReplaceServiceLines_RejectsAircraftPerLanding()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Staff(), TestData.Now);

        var replace = workOrder.ReplaceServiceLines([Line(TestData.PerLandingService())], TestData.Now);

        replace.IsFailure.ShouldBeTrue();
        replace.Error.Code.ShouldBe("Operations.PerLanding.NotPerformable");
    }
}
