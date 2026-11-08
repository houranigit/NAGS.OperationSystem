using BuildingBlocks.Domain.Results;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightServiceTests
{
    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("zero-duration")]
    public void TimeWindow_AllowsOpenClosedAndZeroDurationWindows_AndNormalizesUtc(string kind)
    {
        var from = TestData.Now.ToOffset(TimeSpan.FromHours(3));
        DateTimeOffset? to = kind == "open" ? null : kind == "closed" ? from.AddHours(1) : from;

        var window = TimeWindow.Create(from, to).Value;

        window.FromUtc.ShouldBe(TestData.Now);
        window.FromUtc.Offset.ShouldBe(TimeSpan.Zero);
        window.ToUtc.ShouldBe(to?.ToUniversalTime());
        if (window.ToUtc.HasValue)
            window.ToUtc.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(true, "Operations.TimeWindow.StartRequired")]
    [InlineData(false, "Operations.TimeWindow.EndBeforeStart")]
    public void TimeWindow_RejectsMissingStartAndEndBeforeStart(bool missingStart, string errorCode)
    {
        var result = TimeWindow.Create(missingStart ? default : TestData.Now, TestData.Now.AddMinutes(-1));

        result.Error.Code.ShouldBe(errorCode);
    }

    [Fact]
    public void AttachmentDetails_TrimsItsStorageMetadataAndPreservesContentTypeCase()
    {
        var details = AttachmentDetails.Create(" Evidence.jpg ", " operations/evidence.jpg ", " Image/JPEG ", 200).Value;

        details.FileName.ShouldBe("Evidence.jpg");
        details.StorageKey.ShouldBe("operations/evidence.jpg");
        details.ContentType.ShouldBe("Image/JPEG");
        details.SizeBytes.ShouldBe(200);
    }

    [Theory]
    [InlineData(null, "key", "image/jpeg", 200, "Operations.AttachmentDetails.FileNameRequired")]
    [InlineData("evidence.jpg", " \t ", "image/jpeg", 200, "Operations.AttachmentDetails.StorageKeyRequired")]
    [InlineData("evidence.jpg", "key", null, 200, "Operations.AttachmentDetails.ContentTypeRequired")]
    [InlineData("evidence.jpg", "key", "image/jpeg", 0, "Operations.AttachmentDetails.SizeInvalid")]
    [InlineData("evidence.jpg", "key", "image/jpeg", -1, "Operations.AttachmentDetails.SizeInvalid")]
    public void AttachmentDetails_RequiresFileAndStorageMetadataAndPositiveSize(string? fileName, string? storageKey,
        string? contentType, long size, string errorCode)
    {
        var result = AttachmentDetails.Create(fileName, storageKey, contentType, size);

        result.Error.Code.ShouldBe(errorCode);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(" \t ", " ", null, null)]
    [InlineData(" CUSTOMER-42 ", " Work completed ", "CUSTOMER-42", "Work completed")]
    public void AddService_RecordsActualWorkWithoutChangingPlanning_AndAllowsOptionalTextAndAnOpenWindow(
        string? customerReference, string? notes, string? expectedReference, string? expectedNotes)
    {
        var flight = ScheduleFlight();
        var plannedService = flight.PlannedServices.Single();
        var actualService = TestData.Service();
        var window = TimeWindow.Create(TestData.Now.AddHours(-3), null).Value;
        var userId = Guid.NewGuid();
        var now = TestData.Now.ToOffset(TimeSpan.FromHours(3));

        var service = flight.AddService(actualService, customerReference, window, notes, userId, now).Value;

        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        service.Id.ShouldNotBe(Guid.Empty);
        service.FlightId.ShouldBe(flight.Id);
        service.Service.ShouldBe(actualService);
        service.Window.ShouldBe(window);
        service.CustomerReferenceNumber.ShouldBe(expectedReference);
        service.Notes.ShouldBe(expectedNotes);
        service.EndedByUserId.ShouldBeNull();
        service.CreatedByUserId.ShouldBe(userId);
        service.CreatedAtUtc.ShouldBe(TestData.Now);
        service.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        service.UpdatedByUserId.ShouldBeNull();
        service.UpdatedAtUtc.ShouldBeNull();
        service.Performers.ShouldBeEmpty();
        service.Attachments.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
        flight.PlannedServices.ShouldHaveSingleItem().ShouldBeSameAs(plannedService);
        flight.Schedule.ShouldBe(TestData.Schedule());
        flight.ArrivalSubmissions.ShouldBeEmpty();
        flight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public void Service_CanBeClosedByAnotherUser_AndOtherEditsPreserveItsCreatorAndEndActor()
    {
        var flight = ScheduleFlight();
        var starter = Guid.NewGuid();
        var closer = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var service = flight.AddService(TestData.Service(), null, Window(), null, starter, TestData.Now).Value;
        var closedWindow = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

        flight.UpdateService(service.Id, service.Service, null, closedWindow, null, closer, TestData.Now.AddHours(1))
            .IsSuccess.ShouldBeTrue();

        service.EndedByUserId.ShouldBe(closer);
        service.CreatedByUserId.ShouldBe(starter);
        service.UpdatedByUserId.ShouldBe(closer);

        var correctedStart = TimeWindow.Create(TestData.Now.AddMinutes(-5), closedWindow.ToUtc).Value;
        flight.UpdateService(service.Id, service.Service, "REF-42", correctedStart, "Corrected notes", editor,
            TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        var performer = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), editor,
            TestData.Now.AddHours(2).AddMinutes(1)).Value;
        flight.UpdateServicePerformer(service.Id, performer.Id, performer.Employee, closedWindow, editor,
            TestData.Now.AddHours(2).AddMinutes(2)).IsSuccess.ShouldBeTrue();
        flight.RemoveServicePerformer(service.Id, performer.Id, editor,
            TestData.Now.AddHours(2).AddMinutes(3)).IsSuccess.ShouldBeTrue();
        var attachment = flight.AddServiceAttachment(service.Id, Attachment(), editor,
            TestData.Now.AddHours(2).AddMinutes(4)).Value;
        flight.RemoveServiceAttachment(service.Id, attachment.Id, editor,
            TestData.Now.AddHours(2).AddMinutes(5)).IsSuccess.ShouldBeTrue();

        service.CreatedByUserId.ShouldBe(starter);
        service.EndedByUserId.ShouldBe(closer);
        service.UpdatedByUserId.ShouldBe(editor);
    }

    [Fact]
    public void AddService_WithAClosedWindow_RecordsTheRecordingUserAsCreatorAndEndActor()
    {
        var flight = ScheduleFlight();
        var user = Guid.NewGuid();
        var window = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;

        var service = flight.AddService(TestData.Service(), null, window, null, user, TestData.Now.AddHours(2)).Value;

        service.CreatedByUserId.ShouldBe(user);
        service.EndedByUserId.ShouldBe(user);
        service.Window.ShouldBe(window);
    }

    [Fact]
    public void UpdateService_ChangingTheEndTimeUpdatesOnlyTheEndActor_AndEquivalentTimesAreANoOp()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var starter = service.CreatedByUserId;
        var firstCloser = Guid.NewGuid();
        var correctingUser = Guid.NewGuid();
        var correctedAt = TestData.Now.AddHours(3);
        flight.UpdateService(service.Id, service.Service, null,
            TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value, null, firstCloser,
            TestData.Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        flight.UpdateService(service.Id, service.Service, null,
            TimeWindow.Create(TestData.Now, TestData.Now.AddHours(2)).Value, null, correctingUser,
            correctedAt).IsSuccess.ShouldBeTrue();

        service.CreatedByUserId.ShouldBe(starter);
        service.EndedByUserId.ShouldBe(correctingUser);

        flight.UpdateService(service.Id, service.Service, null,
            TimeWindow.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)),
                TestData.Now.AddHours(2).ToOffset(TimeSpan.FromHours(3))).Value,
            null, Guid.NewGuid(), correctedAt.AddHours(1)).IsSuccess.ShouldBeTrue();

        service.CreatedByUserId.ShouldBe(starter);
        service.EndedByUserId.ShouldBe(correctingUser);
        service.UpdatedByUserId.ShouldBe(correctingUser);
        service.UpdatedAtUtc.ShouldBe(correctedAt);
        flight.UpdatedAtUtc.ShouldBe(correctedAt);
    }

    [Fact]
    public void UpdateService_ClearingTheEndTimeClearsTheEndActor_AndClosingAgainRecordsTheNewCloser()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var starter = service.CreatedByUserId;
        var closedWindow = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        flight.UpdateService(service.Id, service.Service, null, closedWindow, null, Guid.NewGuid(),
            TestData.Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        flight.UpdateService(service.Id, service.Service, null, Window(), null, Guid.NewGuid(),
            TestData.Now.AddHours(2)).IsSuccess.ShouldBeTrue();

        service.CreatedByUserId.ShouldBe(starter);
        service.Window.ToUtc.ShouldBeNull();
        service.EndedByUserId.ShouldBeNull();

        var newCloser = Guid.NewGuid();
        flight.UpdateService(service.Id, service.Service, null, closedWindow, null, newCloser,
            TestData.Now.AddHours(3)).IsSuccess.ShouldBeTrue();

        service.CreatedByUserId.ShouldBe(starter);
        service.EndedByUserId.ShouldBe(newCloser);
    }

    [Fact]
    public void ServicesAndPerformers_AllowRepeatedServiceOccurrencesAndEmployeeWorkPeriods()
    {
        var flight = ScheduleFlight();
        var selectedService = TestData.Service();
        var first = flight.AddService(selectedService, null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var second = flight.AddService(selectedService, null, Window(), null, Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var employee = TestData.Staff();
        var firstPeriod = flight.AddServicePerformer(first.Id, employee, Window(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).Value;
        var secondWindow = TimeWindow.Create(TestData.Now.AddHours(2), null).Value;
        var secondPeriod = flight.AddServicePerformer(first.Id, employee, secondWindow, Guid.NewGuid(), TestData.Now.AddMinutes(3)).Value;

        flight.Services.Count.ShouldBe(2);
        first.Id.ShouldNotBe(second.Id);
        first.Service.ShouldBe(second.Service);
        first.Performers.Count.ShouldBe(2);
        firstPeriod.Id.ShouldNotBe(secondPeriod.Id);
        firstPeriod.Employee.ShouldBe(employee);
        secondPeriod.Employee.ShouldBe(employee);
        secondPeriod.Window.ShouldBe(secondWindow);
        second.Performers.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateService_PreservesItsIdentityCreatorPerformersAndAttachments()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var originalId = service.Id;
        var creator = service.CreatedByUserId;
        var performer = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var attachment = flight.AddServiceAttachment(service.Id, Attachment(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).Value;
        var changedService = TestData.Service();
        var changedWindow = TimeWindow.Create(TestData.Now.AddHours(3), TestData.Now.AddHours(4)).Value;
        var editor = Guid.NewGuid();
        var updatedAt = TestData.Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(3));

        var updated = flight.UpdateService(service.Id, changedService, " NEW-REF ", changedWindow, " Updated work ", editor, updatedAt).Value;

        updated.ShouldBeSameAs(service);
        updated.Id.ShouldBe(originalId);
        updated.Service.ShouldBe(changedService);
        updated.Window.ShouldBe(changedWindow);
        updated.CustomerReferenceNumber.ShouldBe("NEW-REF");
        updated.Notes.ShouldBe("Updated work");
        updated.CreatedByUserId.ShouldBe(creator);
        updated.CreatedAtUtc.ShouldBe(TestData.Now);
        updated.UpdatedByUserId.ShouldBe(editor);
        updated.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        updated.UpdatedAtUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        updated.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        updated.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        flight.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
    }

    [Fact]
    public void UpdateService_EquivalentValuesDoNotChangeAuditTimestampsOrChildren()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var performer = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var attachment = flight.AddServiceAttachment(service.Id, Attachment(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).Value;
        var previousUser = service.UpdatedByUserId;
        var previousServiceTime = service.UpdatedAtUtc;
        var previousFlightTime = flight.UpdatedAtUtc;
        var equivalentService = new ServiceSnapshot(service.Service.ServiceId, service.Service.Name);
        var equivalentWindow = TimeWindow.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), null).Value;

        var result = flight.UpdateService(service.Id, equivalentService, " ", equivalentWindow, " ", Guid.NewGuid(), TestData.Now.AddMinutes(3));

        result.Value.ShouldBeSameAs(service);
        service.UpdatedByUserId.ShouldBe(previousUser);
        service.UpdatedAtUtc.ShouldBe(previousServiceTime);
        flight.UpdatedAtUtc.ShouldBe(previousFlightTime);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
    }

    [Fact]
    public void PerformerEditsAndRemoval_KeepOtherWorkPeriodsAndAttachments_WithoutWindowContainmentRules()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var employee = TestData.Staff();
        var outsideWindow = TimeWindow.Create(TestData.Now.AddHours(-4), TestData.Now.AddHours(-3)).Value;
        var first = flight.AddServicePerformer(service.Id, employee, outsideWindow, Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var second = flight.AddServicePerformer(service.Id, employee, Window(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).Value;
        var attachment = flight.AddServiceAttachment(service.Id, Attachment(), Guid.NewGuid(), TestData.Now.AddMinutes(3)).Value;
        var changedEmployee = TestData.Staff();
        var changedWindow = TimeWindow.Create(TestData.Now.AddHours(3), null).Value;
        var editor = Guid.NewGuid();
        var updatedAt = TestData.Now.AddMinutes(4).ToOffset(TimeSpan.FromHours(3));

        var updated = flight.UpdateServicePerformer(service.Id, first.Id, changedEmployee, changedWindow, editor, updatedAt).Value;

        updated.ShouldBeSameAs(first);
        updated.FlightServiceId.ShouldBe(service.Id);
        updated.Employee.ShouldBe(changedEmployee);
        updated.Window.ShouldBe(changedWindow);
        service.UpdatedByUserId.ShouldBe(editor);
        service.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        flight.UpdatedAtUtc.ShouldBe(updatedAt.ToUniversalTime());
        flight.RemoveServicePerformer(service.Id, first.Id, editor, updatedAt.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(second);
        second.Employee.ShouldBe(employee);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        service.UpdatedAtUtc.ShouldBe(updatedAt.AddMinutes(1).ToUniversalTime());
        flight.UpdatedAtUtc.ShouldBe(updatedAt.AddMinutes(1).ToUniversalTime());
    }

    [Fact]
    public void UpdateServicePerformer_EquivalentValuesDoNotChangeTheWorkPeriodOrAuditTimestamps()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var employee = TestData.Staff();
        var performer = flight.AddServicePerformer(service.Id, employee, Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var previousUser = service.UpdatedByUserId;
        var previousTime = service.UpdatedAtUtc;
        var equivalentEmployee = new StaffMemberSnapshot(employee.StaffMemberId, employee.FullName, employee.EmployeeId);

        var result = flight.UpdateServicePerformer(service.Id, performer.Id, equivalentEmployee,
            TimeWindow.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), null).Value, Guid.NewGuid(), TestData.Now.AddMinutes(2));

        result.Value.ShouldBeSameAs(performer);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        service.UpdatedByUserId.ShouldBe(previousUser);
        service.UpdatedAtUtc.ShouldBe(previousTime);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void AttachmentAddAndRemoval_PreserveUploadMetadataOtherFilesAndPerformers()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var performer = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var uploader = Guid.NewGuid();
        var uploadedAt = TestData.Now.AddMinutes(2).ToOffset(TimeSpan.FromHours(3));
        var details = Attachment();
        var first = flight.AddServiceAttachment(service.Id, details, uploader, uploadedAt).Value;
        var second = flight.AddServiceAttachment(service.Id, Attachment("second.jpg"), Guid.NewGuid(), TestData.Now.AddMinutes(3)).Value;
        var remover = Guid.NewGuid();

        var removed = flight.RemoveServiceAttachment(service.Id, first.Id, remover, TestData.Now.AddMinutes(4)).Value;

        removed.ShouldBeSameAs(first);
        removed.FlightServiceId.ShouldBe(service.Id);
        removed.Details.ShouldBe(details);
        removed.UploadedByUserId.ShouldBe(uploader);
        removed.UploadedAtUtc.ShouldBe(uploadedAt.ToUniversalTime());
        removed.UploadedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(second);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        service.UpdatedByUserId.ShouldBe(remover);
        service.UpdatedAtUtc.ShouldBe(TestData.Now.AddMinutes(4));
        flight.UpdatedAtUtc.ShouldBe(TestData.Now.AddMinutes(4));
    }

    [Fact]
    public void ForeignServiceAndChildIdentifiersAreRejectedWithoutChangingEitherService()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var otherService = AddService(flight);
        var otherFlightService = AddService(ScheduleFlight());
        var foreignPerformer = flight.AddServicePerformer(otherService.Id, TestData.Staff(), Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        var foreignAttachment = flight.AddServiceAttachment(otherService.Id, Attachment(), Guid.NewGuid(), TestData.Now.AddMinutes(2)).Value;
        var previousTime = flight.UpdatedAtUtc;
        Result[] failures =
        [
            flight.UpdateService(otherFlightService.Id, TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now.AddMinutes(3)),
            flight.UpdateServicePerformer(service.Id, foreignPerformer.Id, TestData.Staff(), Window(), Guid.NewGuid(), TestData.Now.AddMinutes(3)),
            flight.RemoveServicePerformer(service.Id, foreignPerformer.Id, Guid.NewGuid(), TestData.Now.AddMinutes(3)),
            flight.RemoveServiceAttachment(service.Id, foreignAttachment.Id, Guid.NewGuid(), TestData.Now.AddMinutes(3))
        ];

        failures.Select(result => result.Error.Code).ShouldBe([
            "Operations.FlightService.NotFound", "Operations.FlightService.PerformerNotFound",
            "Operations.FlightService.PerformerNotFound", "Operations.FlightService.AttachmentNotFound"]);
        failures.ShouldAllBe(result => result.Error.Type == ErrorType.NotFound);
        flight.Services.Count.ShouldBe(2);
        service.Performers.ShouldBeEmpty();
        service.Attachments.ShouldBeEmpty();
        otherService.Performers.ShouldHaveSingleItem().ShouldBeSameAs(foreignPerformer);
        otherService.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(foreignAttachment);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Theory]
    [InlineData("missing-service", "Operations.FlightService.ServiceRequired")]
    [InlineData("unidentified-service", "Operations.FlightService.ServiceRequired")]
    [InlineData("missing-window", "Operations.FlightService.WindowRequired")]
    [InlineData("missing-user", "Operations.FlightService.UserRequired")]
    public void AddService_InvalidInputDoesNotAddWorkOrChangeTheFlight(string invalidInput, string errorCode)
    {
        var flight = ScheduleFlight();
        ServiceSnapshot? selectedService = invalidInput switch
        {
            "missing-service" => null,
            "unidentified-service" => new ServiceSnapshot(Guid.Empty, "Service"),
            _ => TestData.Service()
        };

        var result = flight.AddService(selectedService, null, invalidInput == "missing-window" ? null : Window(), null,
            invalidInput == "missing-user" ? Guid.Empty : Guid.NewGuid(), TestData.Now);

        result.Error.Code.ShouldBe(errorCode);
        flight.Services.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void MissingPerformerAndAttachmentDetailsDoNotChangeTheServiceOrItsAuditFields()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var employeeError = flight.AddServicePerformer(service.Id, null, Window(), Guid.NewGuid(), TestData.Now.AddMinutes(1));
        var windowError = flight.AddServicePerformer(service.Id, TestData.Staff(), null, Guid.NewGuid(), TestData.Now.AddMinutes(1));
        var attachmentError = flight.AddServiceAttachment(service.Id, null, Guid.NewGuid(), TestData.Now.AddMinutes(1));

        employeeError.Error.Code.ShouldBe("Operations.FlightService.EmployeeRequired");
        windowError.Error.Code.ShouldBe("Operations.FlightService.WindowRequired");
        attachmentError.Error.Code.ShouldBe("Operations.FlightService.AttachmentRequired");
        service.Performers.ShouldBeEmpty();
        service.Attachments.ShouldBeEmpty();
        service.UpdatedByUserId.ShouldBeNull();
        service.UpdatedAtUtc.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddService_IsBlockedByAnyCancellationSubmissionIncludingPendingConflicts(bool pending)
    {
        var flight = ScheduleFlight();
        var cancellation = CancellationDetails.Create(TestData.Now, "Weather").Value;
        flight.SubmitCancellation(TestData.Staff(), cancellation, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        if (pending)
            flight.SubmitCancellation(TestData.Staff(), cancellation, Guid.NewGuid(), TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var previousTime = flight.UpdatedAtUtc;

        var result = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now.AddMinutes(2));

        result.Error.Code.ShouldBe("Operations.Flight.CancellationSubmitted");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        flight.Services.ShouldBeEmpty();
        flight.CancellationSubmissions.Count.ShouldBe(pending ? 2 : 1);
        flight.CancellationSubmissions.ShouldAllBe(item => item.IsAccepted == !pending);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void ExistingService_BlocksCancellationWithoutRemovingActualWork()
    {
        var flight = ScheduleFlight();
        var service = AddService(flight);
        var previousTime = flight.UpdatedAtUtc;

        var result = flight.SubmitCancellation(TestData.Staff(), CancellationDetails.Create(TestData.Now, null).Value,
            Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe("Operations.CancellationSubmission.OperationsAlreadyRecorded");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        flight.CancellationSubmissions.ShouldBeEmpty();
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    private static Flight ScheduleFlight() =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static FlightService AddService(Flight flight) =>
        flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;

    private static TimeWindow Window() => TimeWindow.Create(TestData.Now, null).Value;

    private static AttachmentDetails Attachment(string fileName = "evidence.jpg") =>
        AttachmentDetails.Create(fileName, $"operations/{fileName}", "image/jpeg", 200).Value;
}
