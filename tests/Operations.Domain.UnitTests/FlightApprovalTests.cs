using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightApprovalTests
{
    private readonly Guid _actor = Guid.NewGuid();
    private readonly StaffMemberSnapshot _staff = TestData.Staff();
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void ApprovalLocksOperationalWorkAndTheRoster()
    {
        var flight = DepartedFlight();
        var reviewer = Guid.NewGuid();
        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.NotApproved);

        flight.Approve(reviewer, "  Flight reviewer  ", Now.ToOffset(TimeSpan.FromHours(3)), 1).IsSuccess.ShouldBeTrue();

        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.Approved);
        flight.ApprovedAtUtc.ShouldBe(Now);
        flight.ApprovedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        flight.ApprovedByUserId.ShouldBe(reviewer);
        flight.ApprovedByDisplayName.ShouldBe("Flight reviewer");
        flight.ApprovalSequence.ShouldBe(1);
        flight.ApprovalNumber.ShouldBe("RUH-0001");
        flight.ChangeFlightNumber(TestData.FlightNo("SV1021"), Now).Error.Code.ShouldBe("Operations.FlightApproval.Locked");
        var assignee = TestData.Staff();
        flight.ReplaceAssignedEmployees([assignee], Now).Error.Code.ShouldBe("Operations.FlightApproval.Locked");
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(_staff);
        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.Approved);
        flight.ApprovalNumber.ShouldBe("RUH-0001");
        flight.SubmitArrival(_staff, Arrival(Now), _actor, Now).Error.Code.ShouldBe("Operations.FlightApproval.Locked");
        flight.SubmitDeparture(_staff, Departure(Now.AddHours(2)), _actor, Now).Error.Code.ShouldBe("Operations.FlightApproval.Locked");

        flight.Unlock(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.Unlocked);
        flight.IsApprovalLocked.ShouldBeFalse();
        flight.ApprovalSequence.ShouldBeNull();
        flight.ApprovalNumber.ShouldBeNull();
        flight.ReplaceAssignedEmployees([assignee], Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(assignee);
        flight.ChangeFlightNumber(TestData.FlightNo("SV1021"), Now.AddMinutes(2)).Error.Code.ShouldBe("Operations.Flight.ScheduleReadOnly");
        flight.ApprovedByUserId.ShouldBe(reviewer);

        flight.Approve(reviewer, "Flight reviewer", Now.AddMinutes(3), 10_000).IsSuccess.ShouldBeTrue();
        flight.ApprovalNumber.ShouldBe("RUH-10000");
    }

    [Theory]
    [InlineData(null, "Operations.FlightApproval.ActivityWindowInvalid")]
    [InlineData(0, "Operations.FlightApproval.ActivityWindowInvalid")]
    [InlineData(61, "Operations.FlightApproval.ActivityWindowInvalid")]
    [InlineData(10, "Operations.FlightApproval.PerformerWindowInvalid")]
    public void ApprovalRejectsOpenOrInvalidWork(int? endMinute, string code)
    {
        var flight = DepartedFlight();
        var window = TimeWindow.Create(Now, endMinute is { } end ? Now.AddMinutes(end) : null).Value;
        flight.AddService(TestData.Service(), null, window, null, _actor, Now).IsSuccess.ShouldBeTrue();

        flight.Approve(_actor, "Reviewer", Now, 1).Error.Code.ShouldBe(code);
        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.NotApproved);
        flight.ApprovedAtUtc.ShouldBeNull();
        flight.ApprovalNumber.ShouldBeNull();
    }

    [Fact]
    public void ApprovalRejectsEqualArrivalAndDeparture()
    {
        var flight = Schedule();
        flight.SubmitArrival(_staff, Arrival(Now), _actor, Now).IsSuccess.ShouldBeTrue();
        flight.SubmitDeparture(_staff, Departure(Now), _actor, Now).IsSuccess.ShouldBeTrue();

        flight.Approve(_actor, "Reviewer", Now, 1).Error.Code.ShouldBe("Operations.FlightApproval.ActualTimesInvalid");
    }

    [Fact]
    public void CancellationCanBeApprovedWithoutActualsAndCannotBeRevertedUntilUnlocked()
    {
        var flight = Schedule();
        var submission = flight.SubmitCancellation(_staff, CancellationDetails.Create(Now, "Canceled").Value, _actor, Now).Value;

        flight.Approve(_actor, "Reviewer", Now, 1).IsSuccess.ShouldBeTrue();
        flight.RevertCancellation(_staff.StaffMemberId, submission.Id, Now, _actor).Error.Code.ShouldBe("Operations.FlightApproval.Locked");
        flight.StartReturnToRamp(RtrArrival(Now), _actor, Now).IsFailure.ShouldBeTrue();

        flight.Unlock(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.RevertCancellation(_staff.StaffMemberId, submission.Id, Now, _actor).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void PendingReturnToRampAllowsMoreClosedOccurrencesAndOnlyLocksPreviouslyApprovedWork()
    {
        var flight = DepartedFlight();
        flight.Approve(_actor, "Reviewer", Now, 1).IsSuccess.ShouldBeTrue();
        var first = CompleteReturnToRamp(flight, Now.AddHours(2));
        flight.Approve(_actor, "Reviewer", Now.AddHours(3), 2).IsSuccess.ShouldBeTrue();
        flight.ApprovalSequence.ShouldBe(1);
        flight.ApprovalNumber.ShouldBe("RUH-0001");

        var second = CompleteReturnToRamp(flight, Now.AddHours(4));
        var thirdArrival = Now.AddHours(5);
        var third = flight.StartReturnToRamp(RtrArrival(thirdArrival), _actor, thirdArrival).Value;

        flight.ApprovalStatus.ShouldBe(FlightApprovalStatus.PendingRtr);
        flight.IsApprovalLocked.ShouldBeTrue();
        flight.ReplaceAssignedEmployees([], Now).Error.Code.ShouldBe("Operations.FlightApproval.Locked");
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(_staff);
        flight.ApprovedReturnToRampSequence.ShouldBe(first.Sequence);
        flight.UpdateReturnToRampArrival(first.Id, RtrArrival(Now.AddHours(2)), _actor, Now).Error.Code.ShouldBe("Operations.FlightApproval.ReturnToRampLocked");
        flight.EnsureApprovalAllowsReturnToRamp(second.Id).IsSuccess.ShouldBeTrue();
        flight.EnsureApprovalAllowsReturnToRamp(third.Id).IsSuccess.ShouldBeTrue();
        flight.Unlock(Now).Error.Code.ShouldBe("Operations.FlightApproval.UnlockNotAllowed");
        flight.Approve(_actor, "Reviewer", Now, 1).Error.Code.ShouldBe("Operations.FlightApproval.ReturnToRampTimesInvalid");

        FinishReturnToRamp(flight, third, thirdArrival);
        flight.Approve(_actor, "Reviewer", Now.AddHours(6), 1).IsSuccess.ShouldBeTrue();
        flight.ApprovedReturnToRampSequence.ShouldBe(third.Sequence);
        flight.EnsureApprovalAllowsReturnToRamp(second.Id).IsFailure.ShouldBeTrue();
        flight.EnsureApprovalAllowsReturnToRamp(third.Id).IsFailure.ShouldBeTrue();
    }

    private Flight DepartedFlight()
    {
        var flight = Schedule();
        flight.SubmitArrival(_staff, Arrival(Now), _actor, Now).IsSuccess.ShouldBeTrue();
        flight.SubmitDeparture(_staff, Departure(Now.AddHours(1)), _actor, Now).IsSuccess.ShouldBeTrue();
        return flight;
    }

    private Flight Schedule() => Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
        TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [_staff], _actor, Now).Value;

    private ArrivalDetails Arrival(DateTimeOffset at) =>
        ArrivalDetails.Create(null, at, TestData.AircraftType(), "HZ-ABC", _staff, null).Value;

    private DepartureDetails Departure(DateTimeOffset at) => DepartureDetails.Create(at, null, _staff, null).Value;

    private RtrArrivalDetails RtrArrival(DateTimeOffset at) => RtrArrivalDetails.Create(at, _staff, null).Value;

    private FlightReturnToRamp CompleteReturnToRamp(Flight flight, DateTimeOffset at)
    {
        var occurrence = flight.StartReturnToRamp(RtrArrival(at), _actor, at).Value;
        FinishReturnToRamp(flight, occurrence, at);
        return occurrence;
    }

    private void FinishReturnToRamp(Flight flight, FlightReturnToRamp occurrence, DateTimeOffset at)
    {
        var window = TimeWindow.Create(at, at.AddMinutes(15)).Value;
        flight.AddTask(FlightTaskType.Minor, null, null, window, null, _actor, at, occurrence.Id,
            [new(_staff, window)]).IsSuccess.ShouldBeTrue();
        var departure = RtrDepartureDetails.Create(at.AddMinutes(30), null, _staff, null).Value;
        flight.CloseReturnToRamp(occurrence.Id, departure, _actor, at.AddMinutes(30)).IsSuccess.ShouldBeTrue();
    }
}
