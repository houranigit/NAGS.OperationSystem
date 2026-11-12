using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightTaskBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_actuals_add_no_schedule_or_arrival_prerequisite()
    {
        var flight = NewFlight(); var actor = Staff();
        FlightTaskBounds.Validate(flight, Input(actor, Now.AddHours(-3), null), actor.StaffMemberId).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, "StartUtc")]
    [InlineData(true, "Performers[0].StartUtc")]
    public void Actual_arrival_and_departure_bound_task_and_employee_periods(bool employee, string field)
    {
        var flight = NewFlight(); var actor = Staff();
        flight.SubmitArrival(actor, ArrivalDetails.Create(null, Now, new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", actor, null).Value, Guid.NewGuid(), Now)
            .IsSuccess.ShouldBeTrue();
        flight.SubmitDeparture(actor, DepartureDetails.Create(Now.AddHours(1), CustomerSignature.Create("s.png", "files/s", "image/png", 42, Now).Value, actor, null).Value, Guid.NewGuid(), Now)
            .IsSuccess.ShouldBeTrue();
        var input = Input(actor, employee ? Now : Now.AddSeconds(-1), null);
        if (employee) input = input with { Performers = [input.Performers.Single() with { StartUtc = Now.AddSeconds(-1) }] };
        FlightTaskBounds.Validate(flight, input, actor.StaffMemberId).Error.Failures!.Keys.ShouldContain(field);
        input = Input(actor, Now, Now.AddHours(1));
        FlightTaskBounds.Validate(flight, input, actor.StaffMemberId).IsSuccess.ShouldBeTrue();
        FlightTaskBounds.Validate(flight, input with { EndUtc = Now.AddHours(1).AddSeconds(1) }, actor.StaffMemberId)
            .Error.Failures!.Keys.ShouldContain("EndUtc");
    }

    [Fact]
    public void Corrected_actual_bounds_include_saved_task_and_staff_but_do_not_bound_open_resource_starts()
    {
        var flight = NewFlight(); var actor = Staff(); var window = TimeWindow.Create(Now, Now.AddMinutes(30)).Value;
        var task = flight.AddTask(FlightTaskType.Major, null, null, window, null, Guid.NewGuid(), Now,
            initialPerformers: [new(actor, window)]).Value;
        // Imported pre-rule work may retain an open resource; bounds do not invent a separate resource actuals policy.
        var resource = flight.AddTaskTool(task.Id, new(Guid.NewGuid(), "Tool"), ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, Now, Now.AddMinutes(20)).Value,
            null, task.CreatedByUserId, Now).Value;
        typeof(FlightTaskTool).GetProperty(nameof(FlightTaskTool.Usage))!.SetValue(resource,
            ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, Now.AddHours(3), null).Value);
        FlightTaskBounds.ValidateExistingTasks(flight, actor.StaffMemberId, departureOverride: Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        var invalid = FlightTaskBounds.ValidateExistingTasks(flight, actor.StaffMemberId, arrivalOverride: Now.AddMinutes(1));
        invalid.Error.Failures!.Keys.ShouldContain($"Tasks[{task.Id}].StartUtc");
        invalid.Error.Failures.Keys.ShouldContain($"Tasks[{task.Id}].Performers[0].StartUtc");
    }

    [Fact]
    public void Departure_requires_task_staff_and_duration_resource_ends()
    {
        var flight = NewFlight(); var actor = Staff(); var open = TimeWindow.Create(Now, null).Value;
        var task = flight.AddTask(FlightTaskType.Major, null, null, open, null, Guid.NewGuid(), Now,
            initialPerformers: [new(actor, open)]).Value;
        var result = FlightTaskBounds.RequireCompletedForDeparture(flight);
        result.Error.Code.ShouldBe("Operations.Departure.TaskWorkIncomplete");
        result.Error.Failures!.Keys.ShouldContain($"Tasks[{task.Id}].EndUtc"); result.Error.Failures.Keys.ShouldContain($"Tasks[{task.Id}].Performers[0].EndUtc");
        var closed = TimeWindow.Create(Now, Now.AddMinutes(30)).Value;
        flight.SaveTask(task.Id, task.TaskType, null, null, closed, null,
            [new(actor, closed, Id: task.Performers.Single().Id)], [new(new(Guid.NewGuid(), "Tool"), ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, Now, Now.AddMinutes(20)).Value)],
            [], [], task.CreatedByUserId, Now).IsSuccess.ShouldBeTrue();
        FlightTaskBounds.RequireCompletedForDeparture(flight).IsSuccess.ShouldBeTrue();
    }

    private static Flight NewFlight() => Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Customer"), new(Guid.NewGuid(), "RUH", "Riyadh"),
        new(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV100", "SV").Value, ScheduledTime.Create(Now.AddHours(2), Now.AddHours(4)).Value,
        null, [new(Guid.NewGuid(), "Planned")], [], Guid.NewGuid(), Now).Value;
    private static StaffMemberSnapshot Staff() => new(Guid.NewGuid(), "Employee", "E1");
    private static SaveFlightTaskRequest Input(StaffMemberSnapshot actor, DateTimeOffset start, DateTimeOffset? end) =>
        new(0, null, start, end, null, null, [new(Guid.NewGuid(), actor.StaffMemberId, start, end)], [], [], []);
}
