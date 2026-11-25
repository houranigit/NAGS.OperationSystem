using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightReturnToRampEditTests
{
    private readonly Guid _actor = Guid.NewGuid();
    private readonly StaffMemberSnapshot _staff = TestData.Staff();
    private static readonly DateTimeOffset Now = TestData.Now;

    [Theory]
    [InlineData(FlightApprovalStatus.NotApproved)]
    [InlineData(FlightApprovalStatus.Unlocked)]
    public void Departure_allows_one_open_occurrence_without_changing_editable_approval_status(FlightApprovalStatus approvalStatus)
    {
        var flight = Flight();
        var id = Guid.NewGuid();
        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.DepartureRequired");
        Depart(flight);
        if (approvalStatus == FlightApprovalStatus.Unlocked)
        {
            Approve(flight);
            flight.Unlock(Now).IsSuccess.ShouldBeTrue();
        }

        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now).IsSuccess.ShouldBeTrue();

        flight.ApprovalStatus.ShouldBe(approvalStatus);
        flight.IsApprovalLocked.ShouldBeFalse();
        flight.ApprovalNumber.ShouldBeNull();
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        flight.ReturnToRamps.ShouldHaveSingleItem().Id.ShouldBe(id);
        flight.LastReturnToRampSequence.ShouldBe(1);
    }

    [Fact]
    public void First_return_uses_the_latest_recorded_departure_when_reports_conflict()
    {
        var flight = Flight();
        Depart(flight);
        var otherEmployee = TestData.Staff();
        var latestDeparture = Now.AddMinutes(5);
        var otherReport = DepartureDetails.Create(latestDeparture, null, otherEmployee, null).Value;
        flight.SubmitDeparture(otherEmployee, otherReport, Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        var id = Guid.NewGuid();

        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now)
            .Error.Code.ShouldBe("Operations.ReturnToRamp.ArrivalBeforeFlightDeparture");
        flight.SaveReturnToRamp(id, Arrival(latestDeparture), null, [], [], _actor, Now)
            .Error.Code.ShouldBe("Operations.ReturnToRamp.ArrivalBeforeFlightDeparture");
        flight.ReturnToRamps.ShouldBeEmpty();

        var saved = flight.SaveReturnToRamp(id, Arrival(latestDeparture.AddMinutes(1)), null, [], [], _actor, Now);
        saved.Value.Sequence.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Correcting_actual_departure_cannot_move_it_to_or_past_the_first_return(int minutesAfterReturn)
    {
        var flight = Flight();
        Depart(flight);
        var id = Guid.NewGuid();
        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now).IsSuccess.ShouldBeTrue();
        var original = flight.DepartureSubmissions.ShouldHaveSingleItem();
        var originalDetails = original.Details;
        var invalid = DepartureDetails.Create(Now.AddMinutes(minutesAfterReturn), null, _staff, null).Value;

        flight.SubmitDeparture(_staff, invalid, _actor, Now.AddMinutes(1), original.Id)
            .Error.Code.ShouldBe("Operations.DepartureSubmission.DepartureAfterReturnToRampArrival");
        original.Details.ShouldBeSameAs(originalDetails);

        var valid = DepartureDetails.Create(Now.AddMinutes(-1), null, _staff, null).Value;
        flight.SubmitDeparture(_staff, valid, _actor, Now.AddMinutes(1), original.Id)
            .Value.Details.ShouldBe(valid);
    }

    [Fact]
    public void Invalid_closure_does_not_mutate_arrival_or_existing_work()
    {
        var flight = Flight(); Depart(flight); Approve(flight); var id = Guid.NewGuid(); var service = Service();
        var saved = flight.SaveReturnToRamp(id, Arrival(), null, [service], [], _actor, Now).Value;
        var prior = saved.ArrivalDetails;
        var rejected = flight.SaveReturnToRamp(id, RtrArrivalDetails.Create(Now.AddMinutes(-5), _staff, "Changed").Value,
            RtrDepartureDetails.Create(Now.AddHours(1), null, _staff, null).Value, [service], [], _actor, Now.AddMinutes(1));
        rejected.IsFailure.ShouldBeTrue(); saved.ArrivalDetails.ShouldBeSameAs(prior);
        saved.DepartureDetails.ShouldBeNull(); flight.ServiceRecords.Single().Window.ToUtc.ShouldBeNull();
    }

    [Fact]
    public void Correction_preserves_record_and_performer_identity_original_author_and_attachments()
    {
        var flight = Flight(); Depart(flight); Approve(flight); var id = Guid.NewGuid(); var service = Service();
        flight.SaveReturnToRamp(id, Arrival(), null, [service], [], _actor, Now).IsSuccess.ShouldBeTrue();
        var original = flight.ServiceRecords.Single(); var attachmentId = Guid.NewGuid();
        flight.AddServiceAttachment(original.Id, AttachmentDetails.Create("photo.png", "fixture/file.png", "image/png", 100, AttachmentKind.Image).Value,
            _actor, Now, id, attachmentId).IsSuccess.ShouldBeTrue();
        var other = Guid.NewGuid(); var close = TimeWindow.Create(Now, Now.AddMinutes(20)).Value;
        var changed = service with { Notes = "Corrected", Window = close, Performers = [service.Performers.Single() with { Window = close }] };
        var result = flight.SaveReturnToRamp(id, Arrival(), RtrDepartureDetails.Create(Now.AddMinutes(30), null, _staff, null).Value,
            [changed], [], other, Now.AddMinutes(30));
        result.IsSuccess.ShouldBeTrue(); result.Value.IsOpen.ShouldBeFalse();
        var current = flight.ServiceRecords.Single(); current.ShouldBeSameAs(original); current.CreatedByUserId.ShouldBe(_actor);
        current.UpdatedByUserId.ShouldBe(other); current.Performers.Single().Id.ShouldBe(service.Performers.Single().Id!.Value);
        current.Attachments.Single().Id.ShouldBe(attachmentId);
        result.Value.CreatedByUserId.ShouldBe(_actor); result.Value.UpdatedByUserId.ShouldBe(other);
    }

    [Fact]
    public void Closed_previous_occurrence_allows_next_and_cannot_be_reopened_under_it()
    {
        var flight = Flight(); Depart(flight); Approve(flight); var first = Guid.NewGuid(); var closed = TimeWindow.Create(Now, Now.AddMinutes(20)).Value;
        var service = Service() with { Window = closed };
        service = service with { Performers = [service.Performers.Single() with { Window = closed }] };
        flight.SaveReturnToRamp(first, Arrival(), RtrDepartureDetails.Create(Now.AddMinutes(30), null, _staff, null).Value, [service], [], _actor, Now).IsSuccess.ShouldBeTrue();
        var second = Guid.NewGuid();
        flight.SaveReturnToRamp(second, Arrival(Now.AddMinutes(29)), null, [], [], _actor, Now)
            .Error.Code.ShouldBe("Operations.ReturnToRamp.ArrivalBeforePreviousDeparture");
        flight.SaveReturnToRamp(second, Arrival(Now.AddMinutes(30)), null, [], [], _actor, Now)
            .Error.Code.ShouldBe("Operations.ReturnToRamp.ArrivalBeforePreviousDeparture");
        var next = flight.SaveReturnToRamp(second, Arrival(Now.AddMinutes(31)), null, [], [], _actor, Now);
        next.Value.Sequence.ShouldBe(2);
        flight.SaveReturnToRamp(first, Arrival(), null, [service], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        flight.ReturnToRamps.Single(r => r.Id == first).IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Reconciliation_cannot_take_work_from_normal_flight_or_another_occurrence()
    {
        var flight = Flight(); Depart(flight);
        var period = TimeWindow.Create(Now.AddMinutes(-20), Now.AddMinutes(-10)).Value;
        var normal = flight.AddService(TestData.Service(), null, period, null, _actor, Now,
            initialPerformers: [new(_staff, period)]).Value;
        Approve(flight);
        var desired = Service() with { Id = normal.Id };
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [desired], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.WorkIdConflict");
        flight.ReturnToRamps.ShouldBeEmpty(); normal.ReturnToRampId.ShouldBeNull();
    }

    [Fact]
    public void Work_children_cannot_be_moved_between_normal_and_return_to_ramp_work()
    {
        var flight = Flight(); Depart(flight); var personId = Guid.NewGuid();
        var period = TimeWindow.Create(Now.AddMinutes(-20), Now.AddMinutes(-10)).Value;
        var normal = flight.AddService(TestData.Service(), null, period, null, _actor, Now,
            initialPerformers: [new(_staff, period, personId)]).Value;
        Approve(flight);
        var requested = Service();
        requested = requested with { Performers = [requested.Performers.Single() with { Id = personId }] };
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [requested], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.ChildIdConflict");
        flight.ReturnToRamps.ShouldBeEmpty(); normal.Performers.Single().Id.ShouldBe(personId);
    }

    private Flight Flight() => Operations.Domain.Flights.Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [_staff], _actor, Now).Value;
    private void Depart(Flight flight)
    {
        var arrival = ArrivalDetails.Create(null, Now.AddMinutes(-20), TestData.AircraftType(), "HZ-ABC", _staff, null).Value;
        flight.SubmitArrival(_staff, arrival, _actor, Now).IsSuccess.ShouldBeTrue();
        flight.SubmitDeparture(_staff, DepartureDetails.Create(Now.AddMinutes(-10), null, _staff, null).Value, _actor, Now).IsSuccess.ShouldBeTrue();
    }

    private void Approve(Flight flight) => flight.Approve(_actor, "Reviewer", Now, 1).IsSuccess.ShouldBeTrue();
    private RtrArrivalDetails Arrival(DateTimeOffset? at = null) => RtrArrivalDetails.Create(at ?? Now, _staff, null).Value;
    private ReturnToRampServiceInput Service() => new(Guid.NewGuid(), TestData.Service(), null, TimeWindow.Create(Now, null).Value, null,
        [new FlightServicePerformerInput(_staff, TimeWindow.Create(Now, null).Value, Guid.NewGuid())]);
}
