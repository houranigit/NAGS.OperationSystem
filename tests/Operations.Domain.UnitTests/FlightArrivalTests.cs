using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightArrivalTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    public void ArrivalDetails_AllowsMissingCustomerReferenceAndRemarks(string? reference, string? remarks)
    {
        var result = ArrivalDetails.Create(reference, TestData.Now, TestData.AircraftType(),
            "HZ-ABC", TestData.Staff(), remarks);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CustomerWorkReferenceNumber.ShouldBeNull();
        result.Value.Remarks.ShouldBeNull();
    }

    [Fact]
    public void ArrivalDetails_TrimsTextAndNormalizesAtaToUtc()
    {
        var aircraft = TestData.AircraftType();
        var receiver = TestData.Staff();
        var ata = TestData.Now.ToOffset(TimeSpan.FromHours(3));

        var details = ArrivalDetails.Create(" REF-123 ", ata, aircraft, " hz-AbC ", receiver, " On stand ").Value;

        details.CustomerWorkReferenceNumber.ShouldBe("REF-123");
        details.Ata.ShouldBe(TestData.Now);
        details.Ata.Offset.ShouldBe(TimeSpan.Zero);
        details.AircraftType.ShouldBe(aircraft);
        details.AircraftTailNumber.ShouldBe("hz-AbC");
        details.ReceivedByEmployee.ShouldBe(receiver);
        details.Remarks.ShouldBe("On stand");
    }

    [Fact]
    public void ArrivalDetails_RequiresAnActualArrivalTime()
    {
        var result = ArrivalDetails.Create(null, default, TestData.AircraftType(), "HZ-ABC", TestData.Staff(), null);

        result.Error.Code.ShouldBe("Operations.ArrivalDetails.AtaRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void ArrivalDetails_RequiresAnAircraftTailNumber(string? tailNumber)
    {
        var result = ArrivalDetails.Create(null, TestData.Now, TestData.AircraftType(), tailNumber, TestData.Staff(), null);

        result.Error.Code.ShouldBe("Operations.ArrivalDetails.AircraftTailNumberRequired");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrivalDetails_RequiresAnIdentifiedAircraftType(bool missing)
    {
        var aircraft = missing ? null : new AircraftTypeSnapshot(Guid.Empty, "Airbus", "A320");

        var result = ArrivalDetails.Create(null, TestData.Now, aircraft, "HZ-ABC", TestData.Staff(), null);

        result.Error.Code.ShouldBe("Operations.ArrivalDetails.AircraftTypeRequired");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrivalDetails_RequiresAnIdentifiedReceivingEmployee(bool missing)
    {
        var receiver = missing ? null : new StaffMemberSnapshot(Guid.Empty, "Receiving Employee", "E1001");

        var result = ArrivalDetails.Create(null, TestData.Now, TestData.AircraftType(), "HZ-ABC", receiver, null);

        result.Error.Code.ShouldBe("Operations.ArrivalDetails.ReceivedByEmployeeRequired");
    }

    [Fact]
    public void SubmitArrival_AcceptsFirstSubmission_WithoutChangingFlightPlanning()
    {
        var plannedAircraft = TestData.AircraftType();
        var flight = ScheduleFlight(plannedAircraft);
        var submitter = TestData.Staff();
        var receiver = TestData.Staff();
        var actualAircraft = TestData.AircraftType();
        var details = Details(actualAircraft, receiver);
        var userId = Guid.NewGuid();
        var submittedAt = TestData.Now.AddMinutes(10);

        var submission = flight.SubmitArrival(submitter, details, userId, submittedAt).Value;

        flight.ArrivalSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(submission);
        submission.Id.ShouldNotBe(Guid.Empty);
        submission.FlightId.ShouldBe(flight.Id);
        submission.SubmittedByEmployee.ShouldBe(submitter);
        submission.Details.ShouldBe(details);
        submission.Details.ReceivedByEmployee.ShouldBe(receiver);
        submission.IsAccepted.ShouldBeTrue();
        submission.CreatedByUserId.ShouldBe(userId);
        submission.CreatedAtUtc.ShouldBe(submittedAt);
        submission.UpdatedByUserId.ShouldBeNull();
        submission.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(submittedAt);
        flight.AircraftType.ShouldBe(plannedAircraft);
        flight.Schedule.ShouldBe(TestData.Schedule());
        flight.PlannedServices.Count.ShouldBe(1);
        flight.AssignedEmployees.ShouldBeEmpty();
    }

    [Fact]
    public void SubmitArrival_SameEmployeeUpdatesTheirExistingSubmissionAndPreservesItsIdentity()
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var creator = Guid.NewGuid();
        var createdAt = TestData.Now.AddMinutes(1);
        var original = flight.SubmitArrival(submitter, Details(), creator, createdAt).Value;
        var originalId = original.Id;
        var updatedSubmitter = new StaffMemberSnapshot(submitter.StaffMemberId, "Updated Name", submitter.EmployeeId);
        var editor = Guid.NewGuid();
        var editedAt = createdAt.AddMinutes(2);
        var changedDetails = Details(tailNumber: "HZ-UPDATED");

        var updated = flight.SubmitArrival(updatedSubmitter, changedDetails, editor, editedAt).Value;

        flight.ArrivalSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        updated.ShouldBeSameAs(original);
        updated.Id.ShouldBe(originalId);
        updated.Details.ShouldBe(changedDetails);
        updated.SubmittedByEmployee.ShouldBe(submitter);
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(createdAt);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(editedAt);
        updated.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(editedAt);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SubmitArrival_DistinctSubmittersKeepTheirInputsAndClearAcceptance_EvenForTheSameReceiver(int count)
    {
        var flight = ScheduleFlight();
        var receiver = TestData.Staff();
        var originalDetails = Details(receiver: receiver, tailNumber: "HZ-FIRST");
        var firstSubmitter = TestData.Staff();
        var first = flight.SubmitArrival(firstSubmitter, originalDetails, Guid.NewGuid(), TestData.Now).Value;

        for (var index = 1; index < count; index++)
        {
            var details = Details(receiver: receiver, tailNumber: $"HZ-{index}");
            flight.SubmitArrival(TestData.Staff(), details, Guid.NewGuid(), TestData.Now.AddMinutes(index))
                .IsSuccess.ShouldBeTrue();
        }

        flight.ArrivalSubmissions.Count.ShouldBe(count);
        flight.ArrivalSubmissions.Select(item => item.Id).Distinct().Count().ShouldBe(count);
        flight.ArrivalSubmissions.All(item => !item.IsAccepted).ShouldBeTrue();
        first.Details.ShouldBe(originalDetails);
        first.SubmittedByEmployee.ShouldBe(firstSubmitter);

        var editedDetails = Details(receiver: receiver, tailNumber: "HZ-EDITED");
        flight.SubmitArrival(firstSubmitter, editedDetails, Guid.NewGuid(), TestData.Now.AddMinutes(10)).IsSuccess.ShouldBeTrue();

        flight.ArrivalSubmissions.Count.ShouldBe(count);
        flight.ArrivalSubmissions.First().ShouldBeSameAs(first);
        first.Details.ShouldBe(editedDetails);
        flight.ArrivalSubmissions.All(item => !item.IsAccepted).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmitArrival_IdenticalValuesAreANoOp_ForSoleAndConflictingSubmissions(bool hasOtherSubmission)
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var aircraft = TestData.AircraftType();
        var receiver = TestData.Staff();
        var details = Details(aircraft, receiver);
        var creator = Guid.NewGuid();
        var first = flight.SubmitArrival(submitter, details, creator, TestData.Now).Value;
        if (hasOtherSubmission)
            flight.SubmitArrival(TestData.Staff(), Details(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var previousUpdatedAt = flight.UpdatedAtUtc;
        var equivalentDetails = ArrivalDetails.Create(" ", TestData.Now.ToOffset(TimeSpan.FromHours(3)), aircraft,
            " HZ-ABC ", receiver, " ").Value;

        var result = flight.SubmitArrival(submitter, equivalentDetails, Guid.NewGuid(), TestData.Now.AddHours(1));

        result.Value.ShouldBeSameAs(first);
        first.CreatedByUserId.ShouldBe(creator);
        first.UpdatedByUserId.ShouldBeNull();
        first.UpdatedAtUtc.ShouldBeNull();
        first.IsAccepted.ShouldBe(!hasOtherSubmission);
        flight.ArrivalSubmissions.Count.ShouldBe(hasOtherSubmission ? 2 : 1);
        flight.UpdatedAtUtc.ShouldBe(previousUpdatedAt);
    }

    [Theory]
    [InlineData("missing-submitter", "Operations.ArrivalSubmission.SubmittedByEmployeeRequired")]
    [InlineData("unidentified-submitter", "Operations.ArrivalSubmission.SubmittedByEmployeeRequired")]
    [InlineData("missing-user", "Operations.ArrivalSubmission.SubmittedByUserRequired")]
    [InlineData("missing-details", "Operations.ArrivalSubmission.DetailsRequired")]
    public void SubmitArrival_InvalidInputDoesNotChangeExistingSubmissions(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        var originalDetails = Details();
        var first = flight.SubmitArrival(TestData.Staff(), originalDetails, Guid.NewGuid(), TestData.Now).Value;
        StaffMemberSnapshot? submitter = invalidInput switch
        {
            "missing-submitter" => null,
            "unidentified-submitter" => new StaffMemberSnapshot(Guid.Empty, "Employee", "E1001"),
            _ => TestData.Staff()
        };
        var userId = invalidInput == "missing-user" ? Guid.Empty : Guid.NewGuid();
        var details = invalidInput == "missing-details" ? null : Details();

        var result = flight.SubmitArrival(submitter, details, userId, TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(errorCode);
        flight.ArrivalSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(first);
        first.Details.ShouldBe(originalDetails);
        first.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void SubmitArrival_AllowsActualArrivalOutsideTheScheduledWindow(int hoursFromSta)
    {
        var flight = ScheduleFlight();
        var ata = flight.Schedule.Sta.AddHours(hoursFromSta);
        var details = ArrivalDetails.Create(null, ata, TestData.AircraftType(), "HZ-ABC", TestData.Staff(), null).Value;

        var result = flight.SubmitArrival(TestData.Staff(), details, Guid.NewGuid(), ata.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Details.Ata.ShouldBe(ata);
        flight.Schedule.ShouldBe(TestData.Schedule());
    }

    [Fact]
    public void SubmitArrival_NormalizesCreationAndUpdateTimestampsToUtc()
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var submittedAt = TestData.Now.ToOffset(TimeSpan.FromHours(3));
        var submission = flight.SubmitArrival(submitter, Details(), Guid.NewGuid(), submittedAt).Value;

        submission.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        flight.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);

        flight.SubmitArrival(submitter, Details(tailNumber: "HZ-UPDATED"), Guid.NewGuid(), submittedAt.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();

        submission.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        flight.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void SubmitArrival_AllowsPerLandingFlightsWithoutAddingAssignedStaff()
    {
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.PerLandingService()], [], Guid.NewGuid(), TestData.Now).Value;

        var result = flight.SubmitArrival(TestData.Staff(), Details(), Guid.NewGuid(), TestData.Now);

        result.IsSuccess.ShouldBeTrue();
        flight.IsPerLanding.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
        flight.PlannedServices.ShouldHaveSingleItem().IsAircraftPerLanding.ShouldBeTrue();
    }

    private static Flight ScheduleFlight(AircraftTypeSnapshot? plannedAircraft = null) =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), plannedAircraft, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static ArrivalDetails Details(
        AircraftTypeSnapshot? aircraft = null,
        StaffMemberSnapshot? receiver = null,
        string tailNumber = "HZ-ABC") =>
        ArrivalDetails.Create(null, TestData.Now, aircraft ?? TestData.AircraftType(), tailNumber, receiver ?? TestData.Staff(), null).Value;
}
