using BuildingBlocks.Domain.Results;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightCancellationTests
{
    [Fact]
    public void CancellationDetails_RequiresACancellationTime()
    {
        var result = CancellationDetails.Create(default, null);

        result.Error.Code.ShouldBe("Operations.CancellationDetails.TimeRequired");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" \t ", null)]
    [InlineData("  Weather conditions  ", "Weather conditions")]
    public void CancellationDetails_NormalizesTimeAndOptionalRemarks(string? remarks, string? expectedRemarks)
    {
        var details = CancellationDetails.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), remarks).Value;

        details.CanceledAtUtc.ShouldBe(TestData.Now);
        details.CanceledAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        details.Remarks.ShouldBe(expectedRemarks);
        details.ShouldBe(CancellationDetails.Create(TestData.Now, expectedRemarks).Value);
    }

    [Fact]
    public void SubmitCancellation_AcceptsTheFirstSubmissionWithoutChangingFlightPlanningOrAssignedStaff()
    {
        var aircraft = TestData.AircraftType();
        var employee = TestData.Staff();
        var flight = ScheduleFlight(aircraft, employee);
        var plannedService = flight.PlannedServices.Single();
        var assignment = flight.AssignedEmployees.Single();
        var details = Cancellation("Weather");
        var submitter = TestData.Staff();
        var userId = Guid.NewGuid();
        var recordedAt = TestData.Now.AddMinutes(2).ToOffset(TimeSpan.FromHours(3));

        var submission = flight.SubmitCancellation(submitter, details, userId, recordedAt).Value;

        flight.CancellationSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(submission);
        submission.Id.ShouldNotBe(Guid.Empty);
        submission.FlightId.ShouldBe(flight.Id);
        submission.SubmittedByEmployee.ShouldBe(submitter);
        submission.Details.ShouldBe(details);
        submission.IsAccepted.ShouldBeTrue();
        submission.CreatedByUserId.ShouldBe(userId);
        submission.CreatedAtUtc.ShouldBe(recordedAt.ToUniversalTime());
        submission.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        submission.UpdatedByUserId.ShouldBeNull();
        submission.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(recordedAt.ToUniversalTime());
        flight.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        flight.AircraftType.ShouldBe(aircraft);
        flight.Schedule.ShouldBe(TestData.Schedule());
        flight.FlightNumber.ShouldBe(TestData.FlightNo());
        flight.PlannedServices.ShouldHaveSingleItem().ShouldBeSameAs(plannedService);
        flight.AssignedEmployees.ShouldHaveSingleItem().ShouldBeSameAs(assignment);
        assignment.Employee.ShouldBe(employee);
        flight.ArrivalSubmissions.ShouldBeEmpty();
        flight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public void SubmitCancellation_AllowsPerLandingWithoutChangingItsDesignationOrRoster()
    {
        var flight = ScheduleFlight(perLanding: true);

        flight.SubmitCancellation(TestData.Staff(), Cancellation(), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();

        flight.IsPerLanding.ShouldBeTrue();
        flight.PlannedServices.ShouldHaveSingleItem().IsAircraftPerLanding.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing-employee", "Operations.CancellationSubmission.SubmittedByEmployeeRequired")]
    [InlineData("unidentified-employee", "Operations.CancellationSubmission.SubmittedByEmployeeRequired")]
    [InlineData("missing-user", "Operations.CancellationSubmission.SubmittedByUserRequired")]
    [InlineData("missing-details", "Operations.CancellationSubmission.DetailsRequired")]
    public void SubmitCancellation_InvalidInputPreservesTheExistingSubmissionAndItsAcceptance(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        var details = Cancellation("Weather");
        var original = flight.SubmitCancellation(TestData.Staff(), details, Guid.NewGuid(), TestData.Now).Value;
        StaffMemberSnapshot? employee = invalidInput switch
        {
            "missing-employee" => null,
            "unidentified-employee" => new StaffMemberSnapshot(Guid.Empty, "Employee", "E1001"),
            _ => TestData.Staff()
        };

        var result = flight.SubmitCancellation(employee, invalidInput == "missing-details" ? null : Cancellation(),
            invalidInput == "missing-user" ? Guid.Empty : Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(errorCode);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        flight.CancellationSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        original.Details.ShouldBe(details);
        original.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void SubmitCancellation_SameEmployeeCorrectsTheirExistingSubmissionAndPreservesItsIdentityAndCreator()
    {
        var flight = ScheduleFlight();
        var employee = TestData.Staff();
        var creator = Guid.NewGuid();
        var original = flight.SubmitCancellation(employee, Cancellation("Weather"), creator, TestData.Now).Value;
        var originalId = original.Id;
        var changedEmployee = new StaffMemberSnapshot(employee.StaffMemberId, "Updated Name", employee.EmployeeId);
        var corrected = CancellationDetails.Create(TestData.Now.AddMinutes(2), "Customer canceled").Value;
        var editor = Guid.NewGuid();
        var updatedAt = TestData.Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(3));

        var updated = flight.SubmitCancellation(changedEmployee, corrected, editor, updatedAt).Value;

        flight.CancellationSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        updated.Id.ShouldBe(originalId);
        updated.Details.ShouldBe(corrected);
        updated.SubmittedByEmployee.ShouldBe(employee);
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(TestData.Now);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        updated.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        updated.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        flight.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmitCancellation_EquivalentValuesAreANoOpRegardlessOfTheCurrentUserOrOtherSubmissions(bool hasOtherSubmission)
    {
        var flight = ScheduleFlight();
        var employee = TestData.Staff();
        var creator = Guid.NewGuid();
        var original = flight.SubmitCancellation(employee, Cancellation("Weather"), creator, TestData.Now).Value;
        if (hasOtherSubmission)
            flight.SubmitCancellation(TestData.Staff(), Cancellation("Independent input"), Guid.NewGuid(), TestData.Now.AddMinutes(1))
                .IsSuccess.ShouldBeTrue();
        var previousUpdatedAt = flight.UpdatedAtUtc;
        var equivalent = CancellationDetails.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), " Weather ").Value;

        var result = flight.SubmitCancellation(employee, equivalent, Guid.NewGuid(), TestData.Now.AddMinutes(2));

        result.Value.ShouldBeSameAs(original);
        original.CreatedByUserId.ShouldBe(creator);
        original.UpdatedByUserId.ShouldBeNull();
        original.UpdatedAtUtc.ShouldBeNull();
        original.IsAccepted.ShouldBe(!hasOtherSubmission);
        flight.CancellationSubmissions.Count.ShouldBe(hasOtherSubmission ? 2 : 1);
        flight.UpdatedAtUtc.ShouldBe(previousUpdatedAt);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SubmitCancellation_DistinctEmployeesKeepTheirInputsAndClearAcceptance_AndEditsKeepTheConflictUnaccepted(int count)
    {
        var flight = ScheduleFlight();
        var employee = TestData.Staff();
        var firstDetails = Cancellation("First employee input");
        var first = flight.SubmitCancellation(employee, firstDetails, Guid.NewGuid(), TestData.Now).Value;
        for (var index = 1; index < count; index++)
            flight.SubmitCancellation(TestData.Staff(), Cancellation($"Employee {index} input"), Guid.NewGuid(), TestData.Now.AddMinutes(index))
                .IsSuccess.ShouldBeTrue();

        flight.CancellationSubmissions.Count.ShouldBe(count);
        flight.CancellationSubmissions.Select(item => item.Id).Distinct().Count().ShouldBe(count);
        flight.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        first.Details.ShouldBe(firstDetails);

        var editedDetails = Cancellation("Edited first input");
        flight.SubmitCancellation(employee, editedDetails, Guid.NewGuid(), TestData.Now.AddMinutes(10)).IsSuccess.ShouldBeTrue();

        flight.CancellationSubmissions.Count.ShouldBe(count);
        flight.CancellationSubmissions.Single(item => item.Id == first.Id).ShouldBeSameAs(first);
        first.Details.ShouldBe(editedDetails);
        flight.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
    }

    [Theory]
    [InlineData("arrival", 1)]
    [InlineData("departure", 1)]
    [InlineData("arrival", 2)]
    [InlineData("departure", 2)]
    [InlineData("both", 2)]
    public void SubmitCancellation_RejectsExistingOperationsIncludingPendingSubmissions_AndPreservesTheirInputs(string operations, int count)
    {
        var flight = ScheduleFlight();
        for (var index = 0; index < count; index++)
        {
            var employee = TestData.Staff();
            if (operations != "departure")
                flight.SubmitArrival(employee, Arrival($"Arrival {index}"), Guid.NewGuid(), TestData.Now.AddMinutes(index))
                    .IsSuccess.ShouldBeTrue();
            if (operations != "arrival")
                flight.SubmitDeparture(employee, Departure($"Departure {index}"), Guid.NewGuid(), TestData.Now.AddMinutes(index))
                    .IsSuccess.ShouldBeTrue();
        }
        var arrivals = flight.ArrivalSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ToArray();
        var departures = flight.DepartureSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ToArray();
        var previousUpdatedAt = flight.UpdatedAtUtc;

        var result = flight.SubmitCancellation(TestData.Staff(), Cancellation("Cancellation attempt"), Guid.NewGuid(), TestData.Now.AddMinutes(6));

        result.Error.Code.ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        flight.CancellationSubmissions.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(previousUpdatedAt);
        flight.ArrivalSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ShouldBe(arrivals);
        flight.DepartureSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ShouldBe(departures);
        flight.ArrivalSubmissions.ShouldAllBe(item => item.IsAccepted == (count == 1));
        flight.DepartureSubmissions.ShouldAllBe(item => item.IsAccepted == (count == 1));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void CancellationSubmissions_BlockArrivalAndDepartureEvenWhenNoCancellationIsAccepted(bool arrival, bool pending)
    {
        var flight = ScheduleFlight();
        flight.SubmitCancellation(TestData.Staff(), Cancellation("First input"), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        if (pending)
            flight.SubmitCancellation(TestData.Staff(), Cancellation("Independent input"), Guid.NewGuid(), TestData.Now.AddMinutes(1))
                .IsSuccess.ShouldBeTrue();
        var cancellations = flight.CancellationSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ToArray();
        var previousUpdatedAt = flight.UpdatedAtUtc;

        Result result = arrival
            ? flight.SubmitArrival(TestData.Staff(), Arrival(), Guid.NewGuid(), TestData.Now.AddMinutes(2))
            : flight.SubmitDeparture(TestData.Staff(), Departure(), Guid.NewGuid(), TestData.Now.AddMinutes(2));

        result.Error.Code.ShouldBe("Operations.Flight.CancellationSubmitted");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        flight.CancellationSubmissions.Select(item => (item.Id, item.Details, item.IsAccepted)).ShouldBe(cancellations);
        flight.CancellationSubmissions.ShouldAllBe(item => item.IsAccepted == !pending);
        flight.UpdatedAtUtc.ShouldBe(previousUpdatedAt);
        flight.ArrivalSubmissions.ShouldBeEmpty();
        flight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "employee", "Operations.ArrivalSubmission.SubmittedByEmployeeRequired")]
    [InlineData(true, "user", "Operations.ArrivalSubmission.SubmittedByUserRequired")]
    [InlineData(true, "details", "Operations.ArrivalSubmission.DetailsRequired")]
    [InlineData(false, "employee", "Operations.DepartureSubmission.SubmittedByEmployeeRequired")]
    [InlineData(false, "user", "Operations.DepartureSubmission.SubmittedByUserRequired")]
    [InlineData(false, "details", "Operations.DepartureSubmission.DetailsRequired")]
    public void InvalidOperationalInputRetainsValidationPrecedenceAndPreservesCancellationSubmissions(bool arrival, string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        var original = flight.SubmitCancellation(TestData.Staff(), Cancellation(), Guid.NewGuid(), TestData.Now).Value;
        var employee = invalidInput == "employee" ? null : TestData.Staff();
        var userId = invalidInput == "user" ? Guid.Empty : Guid.NewGuid();

        Result result = arrival
            ? flight.SubmitArrival(employee, invalidInput == "details" ? null : Arrival(), userId, TestData.Now.AddMinutes(1))
            : flight.SubmitDeparture(employee, invalidInput == "details" ? null : Departure(), userId, TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(errorCode);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        flight.CancellationSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        original.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.ArrivalSubmissions.ShouldBeEmpty();
        flight.DepartureSubmissions.ShouldBeEmpty();
    }

    private static Flight ScheduleFlight(AircraftTypeSnapshot? aircraft = null, StaffMemberSnapshot? employee = null, bool perLanding = false) =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), aircraft, [perLanding ? TestData.PerLandingService() : TestData.Service()],
            employee is null ? [] : [employee], Guid.NewGuid(), TestData.Now).Value;

    private static CancellationDetails Cancellation(string? remarks = null) => CancellationDetails.Create(TestData.Now, remarks).Value;

    private static ArrivalDetails Arrival(string? remarks = null) =>
        ArrivalDetails.Create(null, TestData.Now, TestData.AircraftType(), "HZ-ABC", TestData.Staff(), remarks).Value;

    private static DepartureDetails Departure(string? remarks = null) =>
        DepartureDetails.Create(TestData.Now.AddHours(1), CustomerSignature.Create("customer.png", "signatures/customer.png", "image/png", 200, TestData.Now).Value,
            TestData.Staff(), remarks).Value;
}
