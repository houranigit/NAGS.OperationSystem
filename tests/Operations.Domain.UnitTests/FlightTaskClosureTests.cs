using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightTaskClosureTests
{
    [Theory]
    [InlineData("tool")]
    [InlineData("material")]
    [InlineData("support")]
    public void Task_cannot_close_or_reopen_child_duration_while_closed(string kind)
    {
        var flight = NewFlight(); var actor = Guid.NewGuid(); var open = TimeWindow.Create(TestData.Now, null).Value;
        var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var task = flight.AddTask(FlightTaskType.Major, null, "Original", open, null, actor, TestData.Now).Value;
        var usage = ResourceUsage.Create(ResourceCalculationType.Duration, null, TestData.Now, null).Value;
        var completed = ResourceUsage.Create(ResourceCalculationType.Duration, null, TestData.Now, closed.ToUtc).Value;
        switch (kind)
        {
            case "tool": flight.AddTaskTool(task.Id, new(Guid.NewGuid(), "Tool"), usage, null, actor, TestData.Now).IsSuccess.ShouldBeTrue(); break;
            case "material": flight.AddTaskMaterial(task.Id, new(Guid.NewGuid(), "Material", ResourceCalculationType.Duration), usage, null, actor, TestData.Now).IsSuccess.ShouldBeTrue(); break;
            default: flight.AddTaskGeneralSupport(task.Id, new(Guid.NewGuid(), "Support", ResourceCalculationType.Duration), usage, null, actor, TestData.Now).IsSuccess.ShouldBeTrue(); break;
        }
        flight.UpdateTask(task.Id, task.TaskType, null, "Rejected", closed, null, actor, TestData.Now)
            .Error.Code.ShouldBe("Operations.FlightTask.OpenResourcePeriods");
        task.Window.ShouldBe(open); task.CustomerReferenceNumber.ShouldBe("ORIGINAL");
        void Update(ResourceUsage value)
        {
            Result result = kind switch
            {
                "tool" => flight.UpdateTaskTool(task.Id, task.Tools.Single().Id, task.Tools.Single().Tool, value, null, actor, TestData.Now),
                "material" => flight.UpdateTaskMaterial(task.Id, task.Materials.Single().Id, task.Materials.Single().Material, value, null, actor, TestData.Now),
                _ => flight.UpdateTaskGeneralSupport(task.Id, task.GeneralSupports.Single().Id, task.GeneralSupports.Single().GeneralSupport, value, null, actor, TestData.Now)
            };
            if (task.Window.ToUtc is not null && value.ToUtc is null) result.Error.Code.ShouldBe("Operations.FlightTask.OpenResourcePeriods");
            else result.IsSuccess.ShouldBeTrue();
        }
        Update(completed);
        flight.UpdateTask(task.Id, task.TaskType, null, null, closed, null, actor, TestData.Now).IsSuccess.ShouldBeTrue();
        Update(usage);
        flight.UpdateTask(task.Id, task.TaskType, null, null, open, null, actor, TestData.Now).IsSuccess.ShouldBeTrue();
        Update(usage);
    }

    [Fact]
    public void Other_users_end_protects_task_removal_but_does_not_lock_its_type()
    {
        var flight = NewFlight(); var owner = Guid.NewGuid(); var closer = Guid.NewGuid();
        var task = flight.AddTask(FlightTaskType.Major, null, null, TimeWindow.Create(TestData.Now, null).Value, null, owner, TestData.Now).Value;
        var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        flight.UpdateTask(task.Id, task.TaskType, null, null, closed, null, closer, TestData.Now).IsSuccess.ShouldBeTrue();
        flight.RemoveTask(task.Id, owner, TestData.Now).Error.Code.ShouldBe("Operations.FlightTask.DeleteReadOnly");
        flight.UpdateTask(task.Id, FlightTaskType.Minor, null, null, closed, null, owner, TestData.Now).IsSuccess.ShouldBeTrue();
        task.EndedByUserId.ShouldBe(closer); task.HasOtherUserEnd.ShouldBeTrue();
    }

    private static Flight NewFlight() => Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
        TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;
}
