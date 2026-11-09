using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightDepartureTests
{
    [Theory]
    [InlineData(null, "image/png", 100, "Operations.CustomerSignature.StorageKeyRequired")]
    [InlineData(" \t ", "image/png", 100, "Operations.CustomerSignature.StorageKeyRequired")]
    [InlineData("signatures/customer.png", null, 100, "Operations.CustomerSignature.ContentTypeRequired")]
    [InlineData("signatures/customer.png", " \t ", 100, "Operations.CustomerSignature.ContentTypeRequired")]
    [InlineData("signatures/customer.png", "image/png", 0, "Operations.CustomerSignature.SizeInvalid")]
    [InlineData("signatures/customer.png", "image/png", -1, "Operations.CustomerSignature.SizeInvalid")]
    public void CustomerSignature_RequiresUsableStorageMetadata(string? key, string? contentType, long size, string errorCode)
    {
        var result = CustomerSignature.Create("customer.png", key, contentType, size, TestData.Now);

        result.Error.Code.ShouldBe(errorCode);
    }

    [Theory]
    [InlineData("filename", "Operations.CustomerSignature.FileNameRequired")]
    [InlineData("empty-basename", "Operations.CustomerSignature.FileNameRequired")]
    [InlineData("signed-time", "Operations.CustomerSignature.SignedAtRequired")]
    [InlineData("key-length", "Operations.CustomerSignature.StorageKeyTooLong")]
    [InlineData("mime-length", "Operations.CustomerSignature.ContentTypeTooLong")]
    public void CustomerSignature_RequiresFileNameAndSigningTime_AndEnforcesMetadataLengthLimits(string invalidInput, string code)
    {
        var result = CustomerSignature.Create(
            invalidInput == "filename" ? " " : invalidInput == "empty-basename" ? "folder/" : "customer.png",
            invalidInput == "key-length" ? new string('k', 501) : "signatures/customer.png",
            invalidInput == "mime-length" ? new string('m', 101) : "image/png",
            200, invalidInput == "signed-time" ? default : TestData.Now);

        result.Error.Code.ShouldBe(code);
        CustomerSignature.Create("customer.png", new string('k', 500), new string('m', 100), 200, TestData.Now)
            .IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("folder/")]
    [InlineData("folder\\")]
    public void CustomerSignature_NormalizesFileNamesAndUtcSigningTime_WhileKeepingStorageMetadata(string folder)
    {
        var signedAt = TestData.Now.AddMinutes(-5).ToOffset(TimeSpan.FromHours(3));

        var signature = CustomerSignature.Create($" {folder}{new string('s', 300)}.png ", " signatures/customer.png ",
            " Image/PNG ", 200, signedAt).Value;

        signature.FileName.ShouldBe(new string('s', 255));
        signature.StorageKey.ShouldBe("signatures/customer.png");
        signature.ContentType.ShouldBe("Image/PNG");
        signature.SizeBytes.ShouldBe(200);
        signature.SignedAtUtc.ShouldBe(TestData.Now.AddMinutes(-5));
        signature.SignedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" \t ", null)]
    [InlineData("  Departed on time  ", "Departed on time")]
    public void DepartureDetails_NormalizesTimeAndText_AndRetainsSignatureMetadata(string? remarks, string? expectedRemarks)
    {
        var handler = TestData.Staff();
        var signature = CustomerSignature.Create(" folder/customer.png ", " signatures/customer.png ", " Image/PNG ",
            200, TestData.Now.AddMinutes(-5).ToOffset(TimeSpan.FromHours(3))).Value;
        var atd = TestData.Now.ToOffset(TimeSpan.FromHours(3));

        var details = DepartureDetails.Create(atd, signature, handler, remarks).Value;

        details.Atd.ShouldBe(TestData.Now);
        details.Atd.Offset.ShouldBe(TimeSpan.Zero);
        details.CustomerSignature.ShouldBe(signature);
        details.CustomerSignature.ShouldNotBeSameAs(signature);
        details.CustomerSignature.FileName.ShouldBe("customer.png");
        details.CustomerSignature.StorageKey.ShouldBe("signatures/customer.png");
        details.CustomerSignature.ContentType.ShouldBe("Image/PNG");
        details.CustomerSignature.SizeBytes.ShouldBe(200);
        details.CustomerSignature.SignedAtUtc.ShouldBe(TestData.Now.AddMinutes(-5));
        details.CustomerSignature.SignedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        details.HandledByEmployee.ShouldBe(handler);
        details.Remarks.ShouldBe(expectedRemarks);
    }

    [Theory]
    [InlineData("missing-time", "Operations.DepartureDetails.AtdRequired")]
    [InlineData("missing-signature", "Operations.DepartureDetails.CustomerSignatureRequired")]
    [InlineData("missing-handler", "Operations.DepartureDetails.HandledByEmployeeRequired")]
    [InlineData("unidentified-handler", "Operations.DepartureDetails.HandledByEmployeeRequired")]
    public void DepartureDetails_RequiresActualTimeSignatureAndIdentifiedHandler(string invalidInput, string errorCode)
    {
        var atd = invalidInput == "missing-time" ? default : TestData.Now;
        var signature = invalidInput == "missing-signature" ? null : Signature();
        StaffMemberSnapshot? handler = invalidInput switch
        {
            "missing-handler" => null,
            "unidentified-handler" => new StaffMemberSnapshot(Guid.Empty, "Handler", "E1001"),
            _ => TestData.Staff()
        };

        var result = DepartureDetails.Create(atd, signature, handler, null);

        result.Error.Code.ShouldBe(errorCode);
    }

    [Fact]
    public void SubmitDeparture_AcceptsTheFirstSubmission_AndKeepsPlanningAndTheSeparateHandlingEmployee()
    {
        var aircraft = TestData.AircraftType();
        var flight = ScheduleFlight(aircraft);
        var submitter = TestData.Staff();
        var handler = TestData.Staff();
        var details = Details(handler);
        var userId = Guid.NewGuid();
        var submittedAt = TestData.Now.AddMinutes(10);

        var submission = flight.SubmitDeparture(submitter, details, userId, submittedAt).Value;

        flight.DepartureSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(submission);
        submission.Id.ShouldNotBe(Guid.Empty);
        submission.FlightId.ShouldBe(flight.Id);
        submission.SubmittedByEmployee.ShouldBe(submitter);
        submission.Details.ShouldBe(details);
        submission.Details.HandledByEmployee.ShouldBe(handler);
        submission.IsAccepted.ShouldBeTrue();
        submission.CreatedByUserId.ShouldBe(userId);
        submission.CreatedAtUtc.ShouldBe(submittedAt);
        submission.UpdatedByUserId.ShouldBeNull();
        submission.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(submittedAt);
        flight.AircraftType.ShouldBe(aircraft);
        flight.Schedule.ShouldBe(TestData.Schedule());
        flight.PlannedServices.Count.ShouldBe(1);
        flight.AssignedEmployees.ShouldBeEmpty();
        flight.ArrivalSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public void SubmitDeparture_UpdatesTheSameEmployeesSubmission_PreservingIdentityCreationAndSubmitterSnapshot()
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var creator = Guid.NewGuid();
        var createdAt = TestData.Now.ToOffset(TimeSpan.FromHours(3));
        var original = flight.SubmitDeparture(submitter, Details(), creator, createdAt).Value;
        var originalId = original.Id;
        var changedSubmitter = new StaffMemberSnapshot(submitter.StaffMemberId, "Updated Name", submitter.EmployeeId);
        var changedDetails = Details(remarks: "Corrected departure");
        var editor = Guid.NewGuid();
        var editedAt = createdAt.AddMinutes(1);

        var updated = flight.SubmitDeparture(changedSubmitter, changedDetails, editor, editedAt).Value;

        flight.DepartureSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        updated.ShouldBeSameAs(original);
        updated.Id.ShouldBe(originalId);
        updated.Details.ShouldBe(changedDetails);
        updated.SubmittedByEmployee.ShouldBe(submitter);
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(TestData.Now);
        updated.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(editedAt.ToUniversalTime());
        updated.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        updated.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(editedAt.ToUniversalTime());
        flight.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SubmitDeparture_KeepsDistinctSubmittersInputsForTheSameHandler_AndClearsAcceptance(int count)
    {
        var flight = ScheduleFlight();
        var handler = TestData.Staff();
        var submitter = TestData.Staff();
        var firstDetails = Details(handler, "First input");
        var first = flight.SubmitDeparture(submitter, firstDetails, Guid.NewGuid(), TestData.Now).Value;
        for (var index = 1; index < count; index++)
            flight.SubmitDeparture(TestData.Staff(), Details(handler, $"Input {index}"), Guid.NewGuid(), TestData.Now.AddMinutes(index))
                .IsSuccess.ShouldBeTrue();

        flight.DepartureSubmissions.Count.ShouldBe(count);
        flight.DepartureSubmissions.Select(item => item.Id).Distinct().Count().ShouldBe(count);
        flight.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        first.Details.ShouldBe(firstDetails);

        var editedDetails = Details(handler, "Edited first input");
        flight.SubmitDeparture(submitter, editedDetails, Guid.NewGuid(), TestData.Now.AddMinutes(10)).IsSuccess.ShouldBeTrue();

        flight.DepartureSubmissions.Count.ShouldBe(count);
        flight.DepartureSubmissions.Single(item => item.Id == first.Id).Details.ShouldBe(editedDetails);
        flight.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmitDeparture_EquivalentDetailsAreANoOp_WithOrWithoutOtherSubmissions(bool hasOtherSubmission)
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var handler = TestData.Staff();
        var creator = Guid.NewGuid();
        var original = flight.SubmitDeparture(submitter, Details(handler), creator, TestData.Now).Value;
        if (hasOtherSubmission)
            flight.SubmitDeparture(TestData.Staff(), Details(handler), Guid.NewGuid(), TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var previousUpdatedAt = flight.UpdatedAtUtc;
        var equivalentDetails = DepartureDetails.Create(TestData.Now.AddHours(1).ToOffset(TimeSpan.FromHours(3)),
            Signature(), new StaffMemberSnapshot(handler.StaffMemberId, handler.FullName, handler.EmployeeId), " ").Value;

        var result = flight.SubmitDeparture(submitter, equivalentDetails, Guid.NewGuid(), TestData.Now.AddHours(2));

        result.Value.ShouldBeSameAs(original);
        original.CreatedByUserId.ShouldBe(creator);
        original.UpdatedByUserId.ShouldBeNull();
        original.UpdatedAtUtc.ShouldBeNull();
        original.IsAccepted.ShouldBe(!hasOtherSubmission);
        flight.DepartureSubmissions.Count.ShouldBe(hasOtherSubmission ? 2 : 1);
        flight.UpdatedAtUtc.ShouldBe(previousUpdatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignatureFileNameOrSigningTimeCorrections_UpdateTheExistingSubmission_AndEquivalentMetadataIsANoOp(bool changeFileName)
    {
        var flight = ScheduleFlight();
        var submitter = TestData.Staff();
        var handler = TestData.Staff();
        var creator = Guid.NewGuid();
        var signature = Signature();
        var originalDetails = DepartureDetails.Create(TestData.Now.AddHours(1), signature, handler, null).Value;
        var submission = flight.SubmitDeparture(submitter, originalDetails, creator, TestData.Now).Value;
        var originalId = submission.Id;
        var fileName = changeFileName ? "corrected.png" : signature.FileName;
        var signedAt = changeFileName ? signature.SignedAtUtc : signature.SignedAtUtc.AddMinutes(1);
        var correctedSignature = CustomerSignature.Create(fileName, signature.StorageKey,
            signature.ContentType, signature.SizeBytes, signedAt).Value;
        var correctedDetails = DepartureDetails.Create(originalDetails.Atd, correctedSignature, handler, null).Value;
        var editor = Guid.NewGuid();
        var editedAt = TestData.Now.AddMinutes(2);

        var updated = flight.SubmitDeparture(submitter, correctedDetails, editor, editedAt).Value;

        updated.ShouldBeSameAs(submission);
        updated.Id.ShouldBe(originalId);
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(TestData.Now);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(editedAt);
        updated.IsAccepted.ShouldBeTrue();
        updated.Details.CustomerSignature.ShouldBe(correctedSignature);
        updated.Details.CustomerSignature.ShouldNotBeSameAs(correctedSignature);
        updated.Details.CustomerSignature.ShouldNotBe(signature);
        originalDetails.CustomerSignature.ShouldBe(signature);
        var equivalentSignature = CustomerSignature.Create($"folder/{fileName}", signature.StorageKey,
            signature.ContentType, signature.SizeBytes, signedAt.ToOffset(TimeSpan.FromHours(3))).Value;

        flight.SubmitDeparture(submitter, DepartureDetails.Create(originalDetails.Atd, equivalentSignature, handler, " ").Value,
            Guid.NewGuid(), editedAt.AddMinutes(1)).Value.ShouldBeSameAs(submission);

        submission.UpdatedByUserId.ShouldBe(editor);
        submission.UpdatedAtUtc.ShouldBe(editedAt);
        flight.UpdatedAtUtc.ShouldBe(editedAt);
        flight.DepartureSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(submission);
    }

    [Theory]
    [InlineData("missing-submitter", "Operations.DepartureSubmission.SubmittedByEmployeeRequired")]
    [InlineData("unidentified-submitter", "Operations.DepartureSubmission.SubmittedByEmployeeRequired")]
    [InlineData("missing-user", "Operations.DepartureSubmission.SubmittedByUserRequired")]
    [InlineData("missing-details", "Operations.DepartureSubmission.DetailsRequired")]
    public void SubmitDeparture_InvalidInputLeavesTheExistingSubmissionUnchanged(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        var details = Details();
        var original = flight.SubmitDeparture(TestData.Staff(), details, Guid.NewGuid(), TestData.Now).Value;
        StaffMemberSnapshot? submitter = invalidInput switch
        {
            "missing-submitter" => null,
            "unidentified-submitter" => new StaffMemberSnapshot(Guid.Empty, "Employee", "E1001"),
            _ => TestData.Staff()
        };

        var result = flight.SubmitDeparture(submitter, invalidInput == "missing-details" ? null : Details(),
            invalidInput == "missing-user" ? Guid.Empty : Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(errorCode);
        flight.DepartureSubmissions.ShouldHaveSingleItem().ShouldBeSameAs(original);
        original.Details.ShouldBe(details);
        original.IsAccepted.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrivalAndDepartureAcceptanceRemainIndependent(bool competingArrival)
    {
        var flight = ScheduleFlight();
        var arrival = ArrivalDetails.Create(null, TestData.Now, TestData.AircraftType(), "HZ-ABC", TestData.Staff(), null).Value;
        var acceptedArrival = flight.SubmitArrival(TestData.Staff(), arrival, Guid.NewGuid(), TestData.Now).Value;
        var acceptedDeparture = flight.SubmitDeparture(TestData.Staff(), Details(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;

        if (competingArrival)
            flight.SubmitArrival(TestData.Staff(), arrival, Guid.NewGuid(), TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        else
            flight.SubmitDeparture(TestData.Staff(), Details(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        acceptedArrival.IsAccepted.ShouldBe(!competingArrival);
        acceptedDeparture.IsAccepted.ShouldBe(competingArrival);
        flight.ArrivalSubmissions.Count.ShouldBe(competingArrival ? 2 : 1);
        flight.DepartureSubmissions.Count.ShouldBe(competingArrival ? 1 : 2);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void SubmitDeparture_AllowsActualTimeOutsideTheScheduledWindow(int hoursFromSta)
    {
        var flight = ScheduleFlight();
        var atd = flight.Schedule.Sta.AddHours(hoursFromSta);
        var details = DepartureDetails.Create(atd, Signature(), TestData.Staff(), null).Value;

        var result = flight.SubmitDeparture(TestData.Staff(), details, Guid.NewGuid(), atd.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Details.Atd.ShouldBe(atd);
        flight.Schedule.ShouldBe(TestData.Schedule());
    }

    [Fact]
    public void SubmitDeparture_AllowsPerLandingWithoutAddingAssignedStaff()
    {
        var flight = ScheduleFlight(perLanding: true);

        flight.SubmitDeparture(TestData.Staff(), Details(), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();

        flight.IsPerLanding.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
        flight.PlannedServices.ShouldHaveSingleItem().IsAircraftPerLanding.ShouldBeTrue();
    }

    private static Flight ScheduleFlight(AircraftTypeSnapshot? aircraft = null, bool perLanding = false) =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), aircraft, [perLanding ? TestData.PerLandingService() : TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static CustomerSignature Signature() => CustomerSignature.Create("customer.png", "signatures/customer.png", "image/png", 200, TestData.Now).Value;

    private static DepartureDetails Details(StaffMemberSnapshot? handler = null, string? remarks = null) =>
        DepartureDetails.Create(TestData.Now.AddHours(1), Signature(), handler ?? TestData.Staff(), remarks).Value;
}
