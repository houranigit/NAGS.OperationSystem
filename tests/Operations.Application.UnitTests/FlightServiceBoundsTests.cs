using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightServiceBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_actual_submissions_impose_no_schedule_bounds_or_actual_time_prerequisite()
    {
        var flight = Flight();
        var actor = Staff();
        var input = Input(actor, Now.AddHours(-3), Now.AddHours(-2));

        FlightServiceBounds.Validate(flight, input, actor.StaffMemberId).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, "StartUtc")]
    [InlineData(true, "Performers[0].StartUtc")]
    public void Arrival_only_bounds_each_service_or_employee_start(bool employee, string field)
    {
        var flight = Flight();
        var actor = Staff();
        AddArrival(flight, actor, Now);
        var input = Input(actor, employee ? Now : Now.AddSeconds(-1), null);
        if (employee) input = input with { Performers = [input.Performers.Single() with { StartUtc = Now.AddSeconds(-1) }] };

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Failures!.Keys.ShouldContain(field);
    }

    [Theory]
    [InlineData(false, "StartUtc")]
    [InlineData(true, "Performers[0].StartUtc")]
    public void Departure_bounds_open_service_and_employee_starts_even_without_end_times(bool employee, string field)
    {
        var flight = Flight();
        var actor = Staff();
        AddDeparture(flight, actor, Now);
        var input = Input(actor, employee ? Now.AddMinutes(-1) : Now.AddSeconds(1), null);
        if (employee) input = input with { Performers = [input.Performers.Single() with { StartUtc = Now.AddSeconds(1), EndUtc = null }] };

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Failures!.Keys.ShouldContain(field);
    }

    [Theory]
    [InlineData(false, "EndUtc")]
    [InlineData(true, "Performers[0].EndUtc")]
    public void Departure_bounds_service_end_and_open_services_employee_end_with_field_errors(bool employee, string field)
    {
        var flight = Flight();
        var actor = Staff();
        AddDeparture(flight, actor, Now);
        var input = Input(actor, Now.AddMinutes(-30), employee ? null : Now.AddSeconds(1));
        if (employee) input = input with { Performers = [input.Performers.Single() with { EndUtc = Now.AddSeconds(1) }] };

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.Error.Code.ShouldBe("Operations.FlightService.Validation");
        result.Error.Failures!.Keys.ShouldContain(field);
    }

    [Fact]
    public void Starts_equal_to_ata_and_ends_equal_to_atd_are_allowed_for_service_and_employee()
    {
        var flight = Flight();
        var actor = Staff();
        AddArrival(flight, actor, Now);
        AddDeparture(flight, actor, Now.AddHours(1));
        var input = Input(actor, Now, Now.AddHours(1));

        FlightServiceBounds.Validate(flight, input, actor.StaffMemberId).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Own_actual_reports_override_accepted_reports_independently_of_creation_order()
    {
        var flight = Flight();
        var actor = Staff();
        var other = Staff();
        var acceptedArrival = AddArrival(flight, other, Now.AddHours(-2), Now.AddDays(-2));
        AddArrival(flight, actor, Now.AddHours(-1), Now.AddDays(-1));
        var acceptedDeparture = AddDeparture(flight, other, Now.AddHours(2), Now.AddDays(-2));
        AddDeparture(flight, actor, Now.AddHours(1), Now.AddDays(-1));
        SeedAccepted(acceptedArrival);
        SeedAccepted(acceptedDeparture);
        var input = Input(actor, Now.AddMinutes(-90), Now.AddMinutes(90));

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.Error.Failures!.Keys.ShouldContain("StartUtc");
        result.Error.Failures.Keys.ShouldContain("EndUtc");
    }

    [Fact]
    public void Accepted_actual_reports_override_earliest_when_the_caller_has_no_reports()
    {
        var flight = Flight();
        var actor = Staff();
        AddArrival(flight, Staff(), Now.AddHours(-2), Now.AddDays(-2));
        var acceptedArrival = AddArrival(flight, Staff(), Now.AddHours(-1), Now.AddDays(-1));
        AddDeparture(flight, Staff(), Now.AddHours(2), Now.AddDays(-2));
        var acceptedDeparture = AddDeparture(flight, Staff(), Now.AddHours(1), Now.AddDays(-1));
        SeedAccepted(acceptedArrival);
        SeedAccepted(acceptedDeparture);
        var input = Input(actor, Now.AddMinutes(-90), Now.AddMinutes(90));

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.Error.Failures!.Keys.ShouldContain("StartUtc");
        result.Error.Failures.Keys.ShouldContain("EndUtc");
    }

    [Fact]
    public void Earliest_created_actual_reports_are_used_when_none_are_own_or_accepted()
    {
        var flight = Flight();
        var actor = Staff();
        AddArrival(flight, Staff(), Now.AddHours(-1), Now.AddDays(-2));
        AddArrival(flight, Staff(), Now.AddHours(-2), Now.AddDays(-1));
        AddDeparture(flight, Staff(), Now.AddHours(1), Now.AddDays(-2));
        AddDeparture(flight, Staff(), Now.AddHours(2), Now.AddDays(-1));
        flight.ArrivalSubmissions.ShouldAllBe(report => !report.IsAccepted);
        flight.DepartureSubmissions.ShouldAllBe(report => !report.IsAccepted);
        var input = Input(actor, Now.AddMinutes(-90), Now.AddMinutes(90));

        var result = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);

        result.Error.Failures!.Keys.ShouldContain("StartUtc");
        result.Error.Failures.Keys.ShouldContain("EndUtc");
    }

    [Fact]
    public void Own_arrival_and_other_employees_departure_can_supply_the_two_bounds_independently()
    {
        var flight = Flight();
        var actor = Staff();
        AddArrival(flight, actor, Now);
        AddDeparture(flight, Staff(), Now.AddHours(1));
        var input = Input(actor, Now.AddMinutes(5), Now.AddMinutes(55));

        FlightServiceBounds.Validate(flight, input, actor.StaffMemberId).IsSuccess.ShouldBeTrue();

        var outsideDeparture = input with { EndUtc = Now.AddHours(1).AddSeconds(1) };
        FlightServiceBounds.Validate(flight, outsideDeparture, actor.StaffMemberId).Error.Failures!
            .Keys.ShouldContain("EndUtc");
    }

    private static Flight Flight() => Operations.Domain.Flights.Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"), new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV100", "SV").Value,
        ScheduledTime.Create(Now.AddHours(2), Now.AddHours(4)).Value, null,
        [new ServiceSnapshot(Guid.NewGuid(), "Service")], [], Guid.NewGuid(), Now).Value;

    private static StaffMemberSnapshot Staff() => new(Guid.NewGuid(), "Employee", "E1");

    private static SaveFlightServiceRequest Input(StaffMemberSnapshot actor, DateTimeOffset start, DateTimeOffset? end) =>
        new(Guid.NewGuid(), start, end, null, null, [new(Guid.NewGuid(), actor.StaffMemberId, start, end)]);

    private static FlightArrivalSubmission AddArrival(Flight flight, StaffMemberSnapshot staff, DateTimeOffset ata,
        DateTimeOffset? createdAt = null) => flight.SubmitArrival(staff,
            ArrivalDetails.Create(null, ata, new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", staff, null).Value,
            Guid.NewGuid(), createdAt ?? Now).Value;

    private static FlightDepartureSubmission AddDeparture(Flight flight, StaffMemberSnapshot staff, DateTimeOffset atd,
        DateTimeOffset? createdAt = null) => flight.SubmitDeparture(staff,
            DepartureDetails.Create(atd, CustomerSignature.Create("signature.png", "bounds/signature.png", "image/png", 1, Now).Value,
                staff, null).Value, Guid.NewGuid(), createdAt ?? Now).Value;

    // Seed a persisted acceptance state to exercise selection without adding an approval workflow.
    private static void SeedAccepted(object submission) =>
        submission.GetType().GetProperty("IsAccepted")!.SetValue(submission, true);
}
