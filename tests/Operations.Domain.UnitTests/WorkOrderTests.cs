using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class WorkOrderTests
{
    private static FlightContext Context() =>
        new(Guid.NewGuid(), TestData.Customer(), TestData.Station(), TestData.OperationType(),
            TestData.FlightNo(), TestData.Schedule(), null);

    private static ServiceLineInput Line(ServiceSnapshot service) =>
        new(service, ServiceLineOrigin.Planned, TestData.Now, TestData.Now.AddMinutes(30), null, false, [TestData.Staff()]);

    [Fact]
    public void OpenCompletion_StartsDraft()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Now);
        workOrder.Status.ShouldBe(WorkOrderStatus.Draft);
        workOrder.Type.ShouldBe(WorkOrderType.Completion);
    }

    [Fact]
    public void Submit_Completion_MissingPlannedServices_Fails()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Now);
        var requiredService = Guid.NewGuid();

        var submit = workOrder.Submit([requiredService], isPerLanding: false, TestData.Now);

        submit.IsFailure.ShouldBeTrue();
        submit.Error.Code.ShouldBe("Operations.WorkOrder.MissingPlannedServices");
    }

    [Fact]
    public void Submit_PerLanding_AllowsZeroServices()
    {
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Now);
        var submit = workOrder.Submit([], isPerLanding: true, TestData.Now);
        submit.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Submitted);
    }

    [Fact]
    public void Approve_Completion_RequiresActuals()
    {
        var service = TestData.Service();
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Now);
        workOrder.ReplaceServiceLines([Line(service)], TestData.Now);
        workOrder.Submit([service.ServiceId], isPerLanding: false, TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 1), Guid.NewGuid(), TestData.Now);

        approve.IsFailure.ShouldBeTrue();
        approve.Error.Code.ShouldBe("Operations.WorkOrder.ActualsRequired");
    }

    [Fact]
    public void Approve_Completion_WithActuals_Succeeds()
    {
        var service = TestData.Service();
        var workOrder = WorkOrder.OpenCompletion(Context(), Guid.NewGuid(), TestData.Now);
        workOrder.ReplaceServiceLines([Line(service)], TestData.Now);
        workOrder.SetActualTimes(ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value, TestData.Now);
        workOrder.Submit([service.ServiceId], isPerLanding: false, TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 1), Guid.NewGuid(), TestData.Now);

        approve.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Approved);
        workOrder.Number!.Value.ShouldBe("RUH-0001");
    }

    [Fact]
    public void Cancellation_Approve_RequiresCancellationDetails_ProvidedAtOpen()
    {
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, "Customer canceled");
        var workOrder = WorkOrder.OpenCancellation(Context(), cancellation, Guid.NewGuid(), TestData.Now);
        workOrder.Submit([], isPerLanding: false, TestData.Now);

        var approve = workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 2), Guid.NewGuid(), TestData.Now);

        approve.IsSuccess.ShouldBeTrue();
        workOrder.IsCancellation.ShouldBeTrue();
    }

    [Fact]
    public void ReturnToReview_UnlocksApprovedWorkOrder()
    {
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, null);
        var workOrder = WorkOrder.OpenCancellation(Context(), cancellation, Guid.NewGuid(), TestData.Now);
        workOrder.Submit([], false, TestData.Now);
        workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", 3), Guid.NewGuid(), TestData.Now);

        var ret = workOrder.ReturnToReview(TestData.Now);

        ret.IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Returned);
        workOrder.Number.ShouldBeNull();
        workOrder.IsEditable.ShouldBeTrue();
    }
}
