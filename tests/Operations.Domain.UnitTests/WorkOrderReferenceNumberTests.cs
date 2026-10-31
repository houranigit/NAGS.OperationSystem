using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class WorkOrderReferenceNumberTests
{
    private static readonly TimeWindow Window = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

    [Fact]
    public void References_are_trimmed_and_can_be_updated_and_cleared_without_replacing_activities()
    {
        var service = Service("  SERVICE-1  ");
        var task = Task("  TASK-1  ");
        var workOrder = Submit([service], [task], "  PACKAGE-1  ").Value;
        workOrder.WorkPackageNumber.ShouldBe("PACKAGE-1");
        var savedService = workOrder.ServiceLines.Single();
        var savedTask = workOrder.Tasks.Single();
        savedService.CustomerReferenceNumber.ShouldBe("SERVICE-1");
        savedTask.CustomerReferenceNumber.ShouldBe("TASK-1");

        workOrder.UpdateDetails(WorkOrderType.Completion, workOrder.ActualFlightNumber, null, null, null,
            null, null, [service with { Id = savedService.Id, CustomerReferenceNumber = " SERVICE-2 " }],
            [task with { Id = savedTask.Id, CustomerReferenceNumber = " TASK-2 " }], TestData.Now, " PACKAGE-2 ")
            .IsSuccess.ShouldBeTrue();
        workOrder.WorkPackageNumber.ShouldBe("PACKAGE-2");
        savedService.CustomerReferenceNumber.ShouldBe("SERVICE-2");
        savedTask.CustomerReferenceNumber.ShouldBe("TASK-2");

        workOrder.UpdateDetails(WorkOrderType.Completion, workOrder.ActualFlightNumber, null, null, null,
            null, null, [service with { Id = savedService.Id, CustomerReferenceNumber = "  " }],
            [task with { Id = savedTask.Id, CustomerReferenceNumber = null }], TestData.Now, "  ")
            .IsSuccess.ShouldBeTrue();
        workOrder.WorkPackageNumber.ShouldBeNull();
        workOrder.ServiceLines.Single().ShouldBeSameAs(savedService);
        workOrder.Tasks.Single().ShouldBeSameAs(savedTask);
        savedService.CustomerReferenceNumber.ShouldBeNull();
        savedTask.CustomerReferenceNumber.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, "service")]
    [InlineData(false, "task")]
    [InlineData(true, "service")]
    [InlineData(true, "task")]
    public void Normal_and_return_to_ramp_activities_reject_overlong_references(bool returnToRamp, string kind)
    {
        var reference = new string('R', 201);
        var services = new[] { Service(kind == "service" ? reference : null) };
        var tasks = new[] { Task(kind == "task" ? reference : null) };
        Result result = returnToRamp
            ? Submit([], []).Value.AppendReturnToRamp(new(null, Window, null, services, tasks), Guid.NewGuid(), TestData.Now)
            : Submit(services, tasks);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.CustomerReferenceNumberTooLong");
    }

    [Fact]
    public void Work_package_length_is_validated_before_mutating_an_existing_work_order()
    {
        Submit([], [], new string('P', 201)).Error.Code.ShouldBe("Operations.WorkOrder.WorkPackageNumberTooLong");
        var workOrder = Submit([], [], new string('P', 200)).Value;
        var result = workOrder.UpdateDetails(WorkOrderType.Completion, workOrder.ActualFlightNumber, null,
            null, null, null, null, [], [], TestData.Now, new string('P', 201));
        result.IsFailure.ShouldBeTrue();
        workOrder.WorkPackageNumber.ShouldBe(new string('P', 200));
    }

    private static WorkOrderServiceLineInput Service(string? reference) =>
        new(TestData.Service(), [TestData.Staff()], Window, null, CustomerReferenceNumber: reference);

    private static WorkOrderTaskInput Task(string? reference) =>
        new(null, TaskType.Minor, null, Window, [TestData.Staff()], [], [], [], CustomerReferenceNumber: reference);

    private static Result<WorkOrder> Submit(
        IReadOnlyList<WorkOrderServiceLineInput> services,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        string? workPackageNumber = null)
    {
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
            TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [], null, null,
            Guid.NewGuid(), TestData.Now).Value;
        return WorkOrder.SubmitNew(flight, WorkOrderType.Completion, Guid.NewGuid(), null, null, null,
            null, null, null, null, services, tasks, TestData.Now, workPackageNumber: workPackageNumber);
    }
}
