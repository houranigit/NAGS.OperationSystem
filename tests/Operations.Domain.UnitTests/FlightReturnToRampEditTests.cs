using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightReturnToRampEditTests
{
    private readonly Guid _actor = Guid.NewGuid();
    private readonly StaffMemberSnapshot _staff = TestData.Staff();
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void New_occurrence_requires_departure_and_only_one_open_occurrence_is_allowed()
    {
        var flight = Flight(); var id = Guid.NewGuid();
        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.DepartureRequired");
        Depart(flight);
        flight.SaveReturnToRamp(id, Arrival(), null, [], [], _actor, Now).IsSuccess.ShouldBeTrue();
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        flight.ReturnToRamps.Count.ShouldBe(1); flight.LastReturnToRampSequence.ShouldBe(1);
    }

    [Fact]
    public void Invalid_closure_does_not_mutate_arrival_or_existing_work()
    {
        var flight = Flight(); Depart(flight); var id = Guid.NewGuid(); var service = Service();
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
        var flight = Flight(); Depart(flight); var id = Guid.NewGuid(); var service = Service();
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
        var flight = Flight(); Depart(flight); var first = Guid.NewGuid(); var closed = TimeWindow.Create(Now, Now.AddMinutes(20)).Value;
        var service = Service() with { Window = closed };
        service = service with { Performers = [service.Performers.Single() with { Window = closed }] };
        flight.SaveReturnToRamp(first, Arrival(), RtrDepartureDetails.Create(Now.AddMinutes(30), null, _staff, null).Value, [service], [], _actor, Now).IsSuccess.ShouldBeTrue();
        var next = flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [], [], _actor, Now);
        next.Value.Sequence.ShouldBe(2);
        flight.SaveReturnToRamp(first, Arrival(), null, [service], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        flight.ReturnToRamps.Single(r => r.Id == first).IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Reconciliation_cannot_take_work_from_normal_flight_or_another_occurrence()
    {
        var flight = Flight(); Depart(flight); var normal = flight.AddService(TestData.Service(), null, TimeWindow.Create(Now, null).Value, null, _actor, Now).Value;
        var desired = Service() with { Id = normal.Id };
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [desired], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.WorkIdConflict");
        flight.ReturnToRamps.ShouldBeEmpty(); normal.ReturnToRampId.ShouldBeNull();
    }

    [Fact]
    public void Work_children_cannot_be_moved_between_normal_and_return_to_ramp_work()
    {
        var flight = Flight(); Depart(flight); var personId = Guid.NewGuid();
        var normal = flight.AddService(TestData.Service(), null, TimeWindow.Create(Now, null).Value, null, _actor, Now,
            initialPerformers: [new(_staff, TimeWindow.Create(Now, null).Value, personId)]).Value;
        var requested = Service();
        requested = requested with { Performers = [requested.Performers.Single() with { Id = personId }] };
        flight.SaveReturnToRamp(Guid.NewGuid(), Arrival(), null, [requested], [], _actor, Now).Error.Code.ShouldBe("Operations.ReturnToRamp.ChildIdConflict");
        flight.ReturnToRamps.ShouldBeEmpty(); normal.Performers.Single().Id.ShouldBe(personId);
    }

    private Flight Flight() => Operations.Domain.Flights.Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [_staff], _actor, Now).Value;
    private void Depart(Flight flight) => flight.SubmitDeparture(_staff, DepartureDetails.Create(Now.AddMinutes(-10), null, _staff, null).Value, _actor, Now);
    private RtrArrivalDetails Arrival() => RtrArrivalDetails.Create(Now, _staff, null).Value;
    private ReturnToRampServiceInput Service() => new(Guid.NewGuid(), TestData.Service(), null, TimeWindow.Create(Now, null).Value, null,
        [new FlightServicePerformerInput(_staff, TimeWindow.Create(Now, null).Value, Guid.NewGuid())]);
}
