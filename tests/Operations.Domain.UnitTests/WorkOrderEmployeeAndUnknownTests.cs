using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class WorkOrderEmployeeAndUnknownTests
{
    private static readonly TimeWindow Window = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

    [Theory]
    [InlineData("service")]
    [InlineData("tool")]
    [InlineData("material")]
    [InlineData("support")]
    public void Unknown_items_require_non_whitespace_descriptions(string kind)
    {
        var staff = TestData.Staff();
        var service = new WorkOrderServiceLineInput(
            new ServiceSnapshot(kind == "service" ? WellKnownMasterDataIds.UnknownService : Guid.NewGuid(), "Item"),
            [staff], Window, "  ");
        var task = new WorkOrderTaskInput(null, TaskType.Minor, null, Window, [staff],
            kind == "tool" ? [new(new ToolSnapshot(WellKnownMasterDataIds.UnknownTool, "Unknown Tool"), ResourceUsage.Create(ResourceCalculationType.Duration, null, Window.From, Window.To).Value, "  ")] : [],
            kind == "material" ? [new(new MaterialSnapshot(WellKnownMasterDataIds.UnknownMaterial, "Unknown Material"), ResourceUsage.Create(ResourceCalculationType.Quantity, 1, null, null).Value, "  ")] : [],
            kind == "support" ? [new(new GeneralSupportSnapshot(WellKnownMasterDataIds.UnknownGeneralSupport, "Unknown General Support"), ResourceUsage.Create(ResourceCalculationType.Quantity, 1, null, null).Value, "  ")] : []);

        var result = Submit([service], [task]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.UnknownDescriptionRequired");
    }

    [Theory]
    [InlineData(-1, 30, "Operations.WorkOrder.EmployeeFromBeforeLine")]
    [InlineData(0, 61, "Operations.WorkOrder.EmployeeToAfterLine")]
    public void Employee_periods_must_be_contained_in_services_and_tasks(int fromMinutes, int toMinutes, string code)
    {
        var staff = TestData.Staff();
        var assignments = new[] { new WorkOrderEmployeeAssignmentInput(staff.StaffMemberId, TimeWindow.Create(TestData.Now.AddMinutes(fromMinutes), TestData.Now.AddMinutes(toMinutes)).Value) };
        var service = new WorkOrderServiceLineInput(TestData.Service(), [staff], Window, null, EmployeeAssignments: assignments);
        var task = new WorkOrderTaskInput(null, TaskType.Minor, null, Window, [staff], [], [], [], EmployeeAssignments: assignments);

        Submit([service], []).Error.Code.ShouldBe(code);
        Submit([], [task]).Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Explicit_assignments_must_cover_selected_employees_exactly_once()
    {
        var staff = TestData.Staff();
        var assignment = new WorkOrderEmployeeAssignmentInput(staff.StaffMemberId, Window);
        IReadOnlyList<WorkOrderEmployeeAssignmentInput>[] invalidAssignments = [[], [assignment, assignment], [new(Guid.NewGuid(), Window)]];
        foreach (var assignments in invalidAssignments)
        {
            var line = new WorkOrderServiceLineInput(TestData.Service(), [staff], Window, null, EmployeeAssignments: assignments);
            Submit([line], []).Error.Code.ShouldBe("Operations.WorkOrder.EmployeeAssignmentsRequired");
        }
    }

    [Fact]
    public void Assignments_preserve_each_employee_period_and_legacy_assignments_inherit_parent_period()
    {
        var first = TestData.Staff();
        var second = TestData.Staff();
        var partial = TimeWindow.Create(Window.From.AddMinutes(10), Window.To.AddMinutes(-10)).Value;
        var assignments = new[] { new WorkOrderEmployeeAssignmentInput(first.StaffMemberId, partial), new WorkOrderEmployeeAssignmentInput(second.StaffMemberId, Window) };
        var service = new WorkOrderServiceLineInput(TestData.Service(), [first, second], Window, null, EmployeeAssignments: assignments);
        var task = new WorkOrderTaskInput(null, TaskType.Minor, null, Window, [first, second], [], [], [], EmployeeAssignments: assignments);

        var result = Submit([service], [task]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ServiceLines.Single().PerformedBy.First().Window.ShouldBe(partial);
        result.Value.ServiceLines.Single().PerformedBy.Last().Window.ShouldBe(Window);
        result.Value.Tasks.Single().Employees.First().Window.ShouldBe(partial);
        result.Value.Tasks.Single().Employees.Last().Window.ShouldBe(Window);
        var legacy = Submit([service with { EmployeeAssignments = null }], [task with { EmployeeAssignments = null }]);
        legacy.Value.ServiceLines.Single().PerformedBy.ShouldAllBe(employee => employee.Window == Window);
        legacy.Value.Tasks.Single().Employees.ShouldAllBe(employee => employee.Window == Window);
    }

    [Fact]
    public void Return_to_ramp_uses_the_same_unknown_and_assignment_validation()
    {
        var staff = TestData.Staff();
        var line = new WorkOrderServiceLineInput(new ServiceSnapshot(WellKnownMasterDataIds.UnknownService, "Unknown Service"), [staff], Window, null);
        var result = Submit([], []);
        result.Value.AppendReturnToRamp(new(null, Window, null, [line], []), Guid.NewGuid(), TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_customer_requires_remarks_for_creation_and_update()
    {
        var flight = Flight.ScheduleNew(new CustomerSnapshot(WellKnownMasterDataIds.UnknownCustomer, null, "Unknown Customer"),
            TestData.Station(), TestData.OperationType(), TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [TestData.Staff()], null, null, Guid.NewGuid(), TestData.Now).Value;
        WorkOrder.SubmitNew(flight, WorkOrderType.Completion, Guid.NewGuid(), TestData.Staff(), null, null, null, null, null, " ", [], [], TestData.Now)
            .Error.Code.ShouldBe("Operations.WorkOrder.UnknownCustomerRemarksRequired");
        var workOrder = WorkOrder.SubmitNew(flight, WorkOrderType.Completion, Guid.NewGuid(), TestData.Staff(), null, null, null, null, null, "Customer described", [], [], TestData.Now).Value;
        workOrder.UpdateDetails(WorkOrderType.Completion, TestData.FlightNo(), null, null, null, null, " ", [], [], TestData.Now)
            .Error.Code.ShouldBe("Operations.WorkOrder.UnknownCustomerRemarksRequired");
    }

    private static BuildingBlocks.Domain.Results.Result<WorkOrder> Submit(IReadOnlyList<WorkOrderServiceLineInput> services, IReadOnlyList<WorkOrderTaskInput> tasks)
    {
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [TestData.Staff()], null, null, Guid.NewGuid(), TestData.Now).Value;
        return WorkOrder.SubmitNew(flight, WorkOrderType.Completion, Guid.NewGuid(), TestData.Staff(), null, null, null, null, null, null, services, tasks, TestData.Now);
    }
}
