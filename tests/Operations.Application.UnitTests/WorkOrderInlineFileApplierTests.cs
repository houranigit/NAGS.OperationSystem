using System.Text;
using BuildingBlocks.Application.Abstractions;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderInlineFileApplierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_StoresAndAttachesInlineFileToStableServiceLine()
    {
        var service = new ServiceSnapshot(Guid.NewGuid(), "Marshalling");
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "EMP-100");
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "RJ", "Royal Jordanian"),
            new StationSnapshot(Guid.NewGuid(), "AMM", "Amman"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Turnaround"),
            FlightNumber.Create("RJ123").Value,
            ScheduledTime.Create(Now, Now.AddHours(2)).Value,
            aircraftType: null,
            plannedServices: [service],
            assignedEmployees: [staff],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            staff,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines:
            [
                new WorkOrderServiceLineInput(
                    service,
                    [staff],
                    TimeWindow.Create(Now, Now.AddMinutes(30)).Value,
                    "Handled")
            ],
            tasks: [],
            Now).Value;
        var serviceLineId = workOrder.ServiceLines.ShouldHaveSingleItem().Id;
        var fileContent = Encoding.ASCII.GetBytes("%PDF-1");
        var payload = new WorkOrderEditableCommandPayload(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            ServiceLines:
            [
                new WorkOrderServiceLineCommand(
                    service.ServiceId,
                    [staff.StaffMemberId],
                    Now,
                    Now.AddMinutes(30),
                    "Handled",
                    Id: serviceLineId,
                    Attachments:
                    [
                        new WorkOrderServiceLineAttachmentCommand(
                            TaskAttachmentKind.Document,
                            Convert.ToBase64String(fileContent),
                            "service-report.pdf",
                            "application/pdf")
                    ])
            ],
            Tasks: []);
        var storage = new RecordingFileStorage();

        var result = await WorkOrderInlineFileApplier.ApplyAsync(
            workOrder,
            payload,
            storage,
            Now.AddMinutes(1),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["work-order-attachments/service-report.pdf"]);
        storage.SavedContainer.ShouldBe("work-order-attachments");
        storage.SavedContent.ShouldBe(fileContent);
        var attachment = workOrder.ServiceLines.ShouldHaveSingleItem()
            .Attachments.ShouldHaveSingleItem();
        attachment.WorkOrderServiceLineId.ShouldBe(serviceLineId);
        attachment.Kind.ShouldBe(TaskAttachmentKind.Document);
        attachment.StorageReference.ShouldBe("work-order-attachments/service-report.pdf");
        attachment.OriginalFileName.ShouldBe("service-report.pdf");
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.Size.ShouldBe(fileContent.Length);
        WorkOrderAttachmentStorage.References(workOrder)
            .ShouldContain("work-order-attachments/service-report.pdf");
    }

    [Fact]
    public async Task Inline_limits_allow_one_maximum_voice_and_reject_aggregate_overflow_before_storage()
    {
        var maximumVoice = new byte[WorkOrderAttachmentPolicy.MaxVoiceBytes];
        maximumVoice[0] = 0x1A;
        maximumVoice[1] = 0x45;
        maximumVoice[2] = 0xDF;
        maximumVoice[3] = 0xA3;
        var encodedVoice = Convert.ToBase64String(maximumVoice);
        var attachment = new WorkOrderTaskAttachmentCommand(
            TaskAttachmentKind.Voice,
            encodedVoice,
            "voice.webm",
            "audio/webm");
        var payload = new WorkOrderEditableCommandPayload(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            ServiceLines: [],
            Tasks:
            [
                new WorkOrderTaskCommand(
                    null,
                    TaskType.Minor,
                    "Voice note",
                    Now,
                    Now.AddMinutes(5),
                    [],
                    [],
                    [],
                    [],
                    [attachment])
            ]);

        WorkOrderAttachmentPolicy.Validate(
                attachment.Kind,
                maximumVoice,
                attachment.FileName,
                attachment.ContentType)
            .IsSuccess.ShouldBeTrue();
        WorkOrderInlineFilePolicy.Validate(payload).IsSuccess.ShouldBeTrue();
        WorkOrderInlineFilePolicy.MaxJsonRequestBytes.ShouldBeGreaterThan(encodedVoice.Length);

        var overflow = payload with
        {
            CustomerSignature = new WorkOrderSignatureCommand("AA==", "signature.png", "image/png")
        };
        var storage = new RecordingFileStorage();
        var workOrder = CreateEmptyWorkOrder();

        var result = await WorkOrderInlineFileApplier.ApplyAsync(
            workOrder,
            overflow,
            storage,
            Now,
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.InlineFilesTooLarge");
        storage.SaveCallCount.ShouldBe(0);
    }

    private static WorkOrder CreateEmptyWorkOrder()
    {
        var service = new ServiceSnapshot(Guid.NewGuid(), "Marshalling");
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "EMP-200");
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "RJ", "Royal Jordanian"),
            new StationSnapshot(Guid.NewGuid(), "AMM", "Amman"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Turnaround"),
            FlightNumber.Create("RJ456").Value,
            ScheduledTime.Create(Now, Now.AddHours(2)).Value,
            aircraftType: null,
            plannedServices: [service],
            assignedEmployees: [staff],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

        return WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            owner: staff,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines:
            [
                new WorkOrderServiceLineInput(
                    service,
                    [staff],
                    TimeWindow.Create(Now, Now.AddMinutes(10)).Value,
                    null)
            ],
            tasks: [],
            Now).Value;
    }

    [Fact]
    public async Task Occurrence_signatures_are_optional_and_independent_and_support_replace_and_remove()
    {
        var workOrder = CreateEmptyWorkOrder();
        var first = AppendOccurrence(workOrder);
        var second = AppendOccurrence(workOrder);
        var storage = new RecordingFileStorage();
        var signature = new WorkOrderSignatureCommand(Convert.ToBase64String([0x89, 0x50, 0x4e, 0x47]), "rtr-one.png", "image/png");
        var payload = OccurrencePayload(first with { CustomerSignature = signature }, second);
        var applied = await WorkOrderInlineFileApplier.ApplyAsync(workOrder, payload, storage, Now, CancellationToken.None);
        applied.IsSuccess.ShouldBeTrue();
        workOrder.ReturnToRamps[0].CustomerSignatureReference.ShouldBe("work-order-signatures/rtr-one.png");
        workOrder.ReturnToRamps[1].CustomerSignatureReference.ShouldBeNull();
        workOrder.CustomerSignatureReference.ShouldBeNull();
        WorkOrderAttachmentStorage.References(workOrder).ShouldContain("work-order-signatures/rtr-one.png");

        // Omitting the signature on an occurrence leaves the existing file attached.
        (await WorkOrderInlineFileApplier.ApplyAsync(workOrder, OccurrencePayload(first, second), storage, Now, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        workOrder.ReturnToRamps[0].CustomerSignatureReference.ShouldBe("work-order-signatures/rtr-one.png");
        storage.SaveCallCount.ShouldBe(1);

        (await WorkOrderInlineFileApplier.ApplyAsync(workOrder, OccurrencePayload(first with { CustomerSignature = signature with { FileName = "replacement.png" } }, second), storage, Now, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        workOrder.ReturnToRamps[0].CustomerSignatureReference.ShouldBe("work-order-signatures/replacement.png");
        WorkOrderAttachmentStorage.References(workOrder).ShouldNotContain("work-order-signatures/rtr-one.png");
        (await WorkOrderInlineFileApplier.ApplyAsync(workOrder, OccurrencePayload(first with { RemoveCustomerSignature = true }, second), storage, Now, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        workOrder.ReturnToRamps[0].CustomerSignatureReference.ShouldBeNull();
        workOrder.ReturnToRamps[0].CustomerSignedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_later_occurrence_signature_cleans_up_previously_stored_files()
    {
        var workOrder = CreateEmptyWorkOrder();
        var first = AppendOccurrence(workOrder);
        var second = AppendOccurrence(workOrder);
        var storage = new RecordingFileStorage();
        var valid = new WorkOrderSignatureCommand(Convert.ToBase64String([0x89, 0x50, 0x4e, 0x47]), "valid.png", "image/png");
        var invalid = valid with { Base64Content = Convert.ToBase64String([1, 2, 3, 4]), FileName = "invalid.png" };
        var result = await WorkOrderInlineFileApplier.ApplyAsync(workOrder,
            OccurrencePayload(first with { CustomerSignature = valid }, second with { CustomerSignature = invalid }), storage, Now, CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.SignatureInvalidSignature");
        storage.DeletedReferences.ShouldBe(["work-order-signatures/valid.png"]);
    }

    [Fact]
    public async Task New_occurrence_signatures_match_creation_sequence_when_timestamps_are_equal()
    {
        var workOrder = CreateEmptyWorkOrder();
        var first = AppendOccurrence(workOrder) with { Id = null };
        var second = AppendOccurrence(workOrder) with { Id = null };
        var signature = new WorkOrderSignatureCommand(Convert.ToBase64String([0x89, 0x50, 0x4e, 0x47]), "first.png", "image/png");
        var result = await WorkOrderInlineFileApplier.ApplyAsync(workOrder,
            OccurrencePayload(first with { CustomerSignature = signature }, second with { CustomerSignature = signature with { FileName = "second.png" } }), new RecordingFileStorage(), Now, CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        workOrder.ReturnToRamps.Single(item => item.Sequence == 1).CustomerSignatureFileName.ShouldBe("first.png");
        workOrder.ReturnToRamps.Single(item => item.Sequence == 2).CustomerSignatureFileName.ShouldBe("second.png");
    }

    [Fact]
    public void Inline_aggregate_limit_includes_occurrence_signatures()
    {
        var signature = new WorkOrderSignatureCommand(Convert.ToBase64String(new byte[WorkOrderInlineFilePolicy.MaxAggregateBytes + 1]), "large.png", "image/png");
        var occurrence = new WorkOrderReturnToRampCommand(null, Now, Now.AddMinutes(10), null, [], [], signature);
        WorkOrderInlineFilePolicy.Validate(OccurrencePayload(occurrence)).Error.Code.ShouldBe("Operations.WorkOrder.InlineFilesTooLarge");
    }

    private static WorkOrderReturnToRampCommand AppendOccurrence(WorkOrder workOrder)
    {
        var service = workOrder.ServiceLines[0];
        var input = new WorkOrderReturnToRampInput(null, service.Window, "Return to ramp",
            [new WorkOrderServiceLineInput(service.Service, service.PerformedBy.Select(item => item.StaffMember).ToList(), service.Window, null)], []);
        var occurrence = workOrder.AppendReturnToRamp(input, Guid.NewGuid(), Now).Value;
        return new WorkOrderReturnToRampCommand(occurrence.Id, occurrence.Window.From, occurrence.Window.To, occurrence.Description,
            [new WorkOrderServiceLineCommand(service.Service.ServiceId, service.PerformedBy.Select(item => item.StaffMember.StaffMemberId).ToList(), service.Window.From, service.Window.To, null, Id: occurrence.ServiceLines[0].Id)], []);
    }

    private static WorkOrderEditableCommandPayload OccurrencePayload(params WorkOrderReturnToRampCommand[] occurrences) =>
        new(null, null, null, null, null, null, null, null, [], [], ReturnToRamps: occurrences);

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> DeletedReferences { get; } = [];
        public int SaveCallCount { get; private set; }
        public string? SavedContainer { get; private set; }
        public byte[]? SavedContent { get; private set; }

        public async Task<StoredFile> SaveAsync(
            string container,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            SavedContainer = container;
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            SavedContent = memory.ToArray();
            return new StoredFile(
                $"{container}/{fileName}",
                contentType,
                SavedContent.LongLength);
        }

        public Task<Stream?> OpenAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            DeletedReferences.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
