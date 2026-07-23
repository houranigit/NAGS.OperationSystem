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

    private sealed class RecordingFileStorage : IFileStorage
    {
        public string? SavedContainer { get; private set; }
        public byte[]? SavedContent { get; private set; }

        public async Task<StoredFile> SaveAsync(
            string container,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
