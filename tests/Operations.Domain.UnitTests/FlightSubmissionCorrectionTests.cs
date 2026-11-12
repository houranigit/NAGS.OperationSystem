using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightSubmissionCorrectionTests
{
    [Fact]
    public void Assigned_employee_corrects_exact_targets_without_replacing_original_attribution_or_acceptance()
    {
        var fixture = new Fixture(assigned: true);
        var correctedAt = TestData.Now.AddMinutes(10);
        var arrivalDetails = fixture.ArrivalDetails("HZ-CORRECTED");
        var departureDetails = fixture.DepartureDetails("Corrected departure");

        var arrival = fixture.Flight.SubmitArrival(fixture.Editor, arrivalDetails, fixture.EditorUserId,
            correctedAt, fixture.Arrival.Id).Value;
        var departure = fixture.Flight.SubmitDeparture(fixture.Editor, departureDetails, fixture.EditorUserId,
            correctedAt, fixture.Departure.Id).Value;

        arrival.ShouldBeSameAs(fixture.Arrival);
        arrival.SubmittedByEmployee.ShouldBe(fixture.Author);
        arrival.CreatedByUserId.ShouldBe(fixture.AuthorUserId);
        arrival.CreatedAtUtc.ShouldBe(TestData.Now);
        arrival.IsAccepted.ShouldBeTrue();
        arrival.UpdatedByUserId.ShouldBe(fixture.EditorUserId);
        arrival.UpdatedAtUtc.ShouldBe(correctedAt);
        arrival.Details.ShouldBe(arrivalDetails);
        arrival.Details.ReceivedByEmployee.ShouldBe(fixture.Author);
        departure.ShouldBeSameAs(fixture.Departure);
        departure.SubmittedByEmployee.ShouldBe(fixture.Author);
        departure.CreatedByUserId.ShouldBe(fixture.AuthorUserId);
        departure.CreatedAtUtc.ShouldBe(TestData.Now);
        departure.IsAccepted.ShouldBeTrue();
        departure.UpdatedByUserId.ShouldBe(fixture.EditorUserId);
        departure.UpdatedAtUtc.ShouldBe(correctedAt);
        departure.Details.ShouldBe(departureDetails);
        departure.Details.HandledByEmployee.ShouldBe(fixture.Author);
        fixture.Flight.ArrivalSubmissions.ShouldHaveSingleItem();
        fixture.Flight.DepartureSubmissions.ShouldHaveSingleItem();
    }

    [Fact]
    public void Foreign_target_requires_assignment_even_for_identical_details()
    {
        var fixture = new Fixture(assigned: false);

        var arrival = fixture.Flight.SubmitArrival(fixture.Editor, fixture.Arrival.Details,
            fixture.EditorUserId, TestData.Now.AddMinutes(10), fixture.Arrival.Id);
        var departure = fixture.Flight.SubmitDeparture(fixture.Editor, fixture.Departure.Details,
            fixture.EditorUserId, TestData.Now.AddMinutes(10), fixture.Departure.Id);

        arrival.Error.Code.ShouldBe("Operations.ArrivalSubmission.AssignmentRequired");
        departure.Error.Code.ShouldBe("Operations.DepartureSubmission.AssignmentRequired");
        fixture.Arrival.UpdatedAtUtc.ShouldBeNull();
        fixture.Departure.UpdatedAtUtc.ShouldBeNull();
        fixture.Flight.ArrivalSubmissions.ShouldHaveSingleItem();
        fixture.Flight.DepartureSubmissions.ShouldHaveSingleItem();
        fixture.Flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void Repeated_foreign_correction_is_a_no_op_and_retains_the_first_editor_timestamp()
    {
        var fixture = new Fixture(assigned: true);
        var correctedAt = TestData.Now.AddMinutes(10);
        var arrivalDetails = fixture.ArrivalDetails("HZ-CORRECTED");
        var departureDetails = fixture.DepartureDetails("Corrected departure");
        fixture.Flight.SubmitArrival(fixture.Editor, arrivalDetails, fixture.EditorUserId,
            correctedAt, fixture.Arrival.Id).IsSuccess.ShouldBeTrue();
        fixture.Flight.SubmitDeparture(fixture.Editor, departureDetails, fixture.EditorUserId,
            correctedAt, fixture.Departure.Id).IsSuccess.ShouldBeTrue();

        fixture.Flight.SubmitArrival(fixture.Editor, arrivalDetails, Guid.NewGuid(), correctedAt.AddMinutes(5),
            fixture.Arrival.Id).Value.ShouldBeSameAs(fixture.Arrival);
        fixture.Flight.SubmitDeparture(fixture.Editor, departureDetails, Guid.NewGuid(), correctedAt.AddMinutes(5),
            fixture.Departure.Id).Value.ShouldBeSameAs(fixture.Departure);

        fixture.Arrival.UpdatedAtUtc.ShouldBe(correctedAt);
        fixture.Arrival.UpdatedByUserId.ShouldBe(fixture.EditorUserId);
        fixture.Departure.UpdatedAtUtc.ShouldBe(correctedAt);
        fixture.Departure.UpdatedByUserId.ShouldBe(fixture.EditorUserId);
        fixture.Flight.UpdatedAtUtc.ShouldBe(correctedAt);
    }

    [Theory]
    [InlineData(false, "NotFound")]
    [InlineData(true, "TargetRequired")]
    public void Missing_or_empty_explicit_target_does_not_create_a_submission(bool empty, string code)
    {
        var fixture = new Fixture(assigned: true);
        var target = empty ? Guid.Empty : Guid.NewGuid();

        var arrival = fixture.Flight.SubmitArrival(fixture.Editor, fixture.ArrivalDetails("HZ-CORRECTED"),
            fixture.EditorUserId, TestData.Now.AddMinutes(10), target);
        var departure = fixture.Flight.SubmitDeparture(fixture.Editor, fixture.DepartureDetails("Correction"),
            fixture.EditorUserId, TestData.Now.AddMinutes(10), target);

        arrival.Error.Code.ShouldBe($"Operations.ArrivalSubmission.{code}");
        departure.Error.Code.ShouldBe($"Operations.DepartureSubmission.{code}");
        fixture.Flight.ArrivalSubmissions.ShouldHaveSingleItem();
        fixture.Flight.DepartureSubmissions.ShouldHaveSingleItem();
        fixture.Arrival.UpdatedAtUtc.ShouldBeNull();
        fixture.Departure.UpdatedAtUtc.ShouldBeNull();
        fixture.Flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    private sealed class Fixture
    {
        public StaffMemberSnapshot Author { get; } = TestData.Staff();
        public StaffMemberSnapshot Editor { get; } = TestData.Staff();
        public Guid AuthorUserId { get; } = Guid.NewGuid();
        public Guid EditorUserId { get; } = Guid.NewGuid();
        public AircraftTypeSnapshot Aircraft { get; } = TestData.AircraftType();
        public Flight Flight { get; }
        public FlightArrivalSubmission Arrival { get; }
        public FlightDepartureSubmission Departure { get; }

        public Fixture(bool assigned)
        {
            Flight = Operations.Domain.Flights.Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
                TestData.FlightNo(), TestData.Schedule(), Aircraft, [TestData.Service()], assigned ? [Editor] : [],
                Guid.NewGuid(), TestData.Now).Value;
            Arrival = Flight.SubmitArrival(Author, ArrivalDetails("HZ-ORIGINAL"), AuthorUserId, TestData.Now).Value;
            Departure = Flight.SubmitDeparture(Author, DepartureDetails("Original departure"), AuthorUserId, TestData.Now).Value;
        }

        public ArrivalDetails ArrivalDetails(string tail) => Operations.Domain.ValueObjects.ArrivalDetails.Create(
            null, TestData.Now, Aircraft, tail, Author, null).Value;

        public DepartureDetails DepartureDetails(string remarks) => Operations.Domain.ValueObjects.DepartureDetails.Create(
            TestData.Now.AddHours(1), CustomerSignature.Create("customer.png", "signatures/customer.png",
                "image/png", 200, TestData.Now).Value, Author, remarks).Value;
    }
}
