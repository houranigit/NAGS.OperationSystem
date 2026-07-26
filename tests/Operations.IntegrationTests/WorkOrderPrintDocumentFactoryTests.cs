using System.Text;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class WorkOrderPrintDocumentFactoryTests
{
    [Fact]
    public void Create_ReturnsBrandedMultipageA4Pdf()
    {
        var source = CreateSource(includeCompletionDetails: true);

        var file = WorkOrderPrintDocumentFactory.Create(source);

        file.FileName.ShouldBe("work-order-HOF-0042.pdf");
        file.Content.Length.ShouldBeGreaterThan(20_000);
        Encoding.ASCII.GetString(file.Content, 0, 4).ShouldBe("%PDF");

        using var stream = new MemoryStream(file.Content, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBeGreaterThanOrEqualTo(2);
        var pages = Enumerable.Range(0, document.PageCount)
            .Select(index => document.Pages[index])
            .ToList();
        foreach (var page in pages)
        {
            page.Width.Point.ShouldBe(595.276, tolerance: 0.1);
            page.Height.Point.ShouldBe(841.89, tolerance: 0.1);
        }
        pages
            .SelectMany(page => ContentReader.ReadContent(page).OfType<COperator>())
            .Count(operation => operation.Name is "Tj" or "TJ")
            .ShouldBeGreaterThan(40);
    }

    [Fact]
    public void Create_ToleratesApprovedPerLandingWorkOrderWithoutActualsAircraftOrSignature()
    {
        var baseline = CreateSource(includeCompletionDetails: false);
        var now = baseline.WorkOrder.ScheduledArrivalUtc;
        var performers = Enumerable.Range(1, 20)
            .Select(index => new WorkOrderServiceLinePerformerDto(
                Guid.NewGuid(),
                $"Long Name Performer {index}",
                $"EMP-{index:D3}"))
            .ToList();
        var source = baseline with
        {
            WorkOrder = baseline.WorkOrder with
            {
                Remarks = $"Long unbroken value: {new string('X', 2_000)}",
                ServiceLines =
                [
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "Aircraft Per Landing",
                        performers,
                        now,
                        now.AddMinutes(45),
                        new string('D', 2_000),
                        false)
                ]
            },
            Flight = baseline.Flight with { IsPerLanding = true }
        };

        var file = WorkOrderPrintDocumentFactory.Create(source);

        Encoding.ASCII.GetString(file.Content, 0, 4).ShouldBe("%PDF");
        file.FileName.ShouldBe("work-order-HOF-0042.pdf");
        using var stream = new MemoryStream(file.Content, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void MergeWorkerWindows_PreservesGapsAndCoalescesOverlaps()
    {
        var start = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        var alexId = Guid.NewGuid();
        var secondAlexId = Guid.NewGuid();
        var source = new[]
        {
            new WorkOrderPrintDocumentFactory.WorkerWindow(alexId, "Alex", "Technician", start, start.AddMinutes(15)),
            new WorkOrderPrintDocumentFactory.WorkerWindow(alexId, "Alex", "Technician", start.AddHours(1), start.AddHours(1).AddMinutes(15)),
            new WorkOrderPrintDocumentFactory.WorkerWindow(alexId, "Alex", "Technician", start.AddHours(1).AddMinutes(10), start.AddHours(1).AddMinutes(30)),
            new WorkOrderPrintDocumentFactory.WorkerWindow(secondAlexId, "Alex", "Engineer", start, start.AddMinutes(10))
        };

        var merged = WorkOrderPrintDocumentFactory.MergeWorkerWindows(source);

        merged.Count.ShouldBe(3);
        merged.Select(window => window.StaffMemberId).Distinct().Count().ShouldBe(2);
        merged.Aggregate(TimeSpan.Zero, (total, window) => total + (window.ToUtc - window.FromUtc))
            .ShouldBe(TimeSpan.FromMinutes(55));
    }

    [Fact]
    public void ReturnWindow_UsesApprovedRecord()
    {
        var baseline = CreateSource(includeCompletionDetails: true);
        var now = baseline.WorkOrder.ScheduledArrivalUtc;
        var serviceLines = Enumerable.Range(1, 7)
            .Select(index => new WorkOrderServiceLineDto(
                Guid.NewGuid(), Guid.NewGuid(), $"Service {index}", PerformedBy(Guid.NewGuid(), $"Staff {index}"),
                now.AddMinutes(index), now.AddMinutes(index + 10), null, index == 7))
            .ToList();
        var workOrder = baseline.WorkOrder with { ServiceLines = serviceLines };

        WorkOrderPrintDocumentFactory.ResolveHeaderTo(workOrder)
            .ShouldBe(now.AddMinutes(17));

        var returnTask = workOrder.Tasks[0] with
        {
            FromUtc = now.AddMinutes(120),
            ToUtc = now.AddMinutes(130),
            IsReturnToRamp = true
        };
        WorkOrderPrintDocumentFactory.ResolveHeaderTo(workOrder with
            {
                Tasks = [returnTask]
            })
            .ShouldBe(now.AddMinutes(130));

        WorkOrderPrintDocumentFactory.ResolveHeaderTo(workOrder with
            {
                ServiceLines = serviceLines.Select(line => line with { IsReturnToRamp = false }).ToList(),
                Tasks = workOrder.Tasks.Select(task => task with { IsReturnToRamp = false }).ToList()
            })
            .ShouldBe(workOrder.ActualDepartureUtc);
    }

    [Fact]
    public void FlightOverview_ShowsOnlyRelevantSecondaryDetails()
    {
        var baseline = CreateSource(includeCompletionDetails: true);

        WorkOrderPrintDocumentFactory.BuildScheduledFlightNote(baseline)
            .ShouldBeNull();
        WorkOrderPrintDocumentFactory.BuildServiceBasisNote(baseline)
            .ShouldBeNull();

        WorkOrderPrintDocumentFactory.BuildScheduledFlightNote(baseline with
            {
                Flight = baseline.Flight with { CurrentFlightNumber = "456" }
            })
            .ShouldBe("Scheduled: RJ-456");

        WorkOrderPrintDocumentFactory.BuildServiceBasisNote(baseline with
            {
                WorkOrder = baseline.WorkOrder with { ServiceLines = [] },
                Flight = baseline.Flight with { IsPerLanding = true }
            })
            .ShouldBe("Service basis: Per Landing");

        WorkOrderPrintDocumentFactory.BuildServiceBasisNote(baseline with
            {
                Flight = baseline.Flight with { IsPerLanding = true }
            })
            .ShouldBe("Service basis: On Call");
    }

    [Fact]
    public void FlightNumber_UsesExactlyOneCarrierCode()
    {
        var workOrder = CreateSource(includeCompletionDetails: true).WorkOrder;

        WorkOrderPrintDocumentFactory.DisplayFlightNumber(workOrder, "123")
            .ShouldBe("RJ-123");
        WorkOrderPrintDocumentFactory.DisplayFlightNumber(workOrder, "RJ123")
            .ShouldBe("RJ-123");
        WorkOrderPrintDocumentFactory.DisplayFlightNumber(workOrder, "rj-123")
            .ShouldBe("RJ-123");
        WorkOrderPrintDocumentFactory.DisplayFlightNumber(workOrder, null)
            .ShouldBe("Not recorded");
    }

    [Fact]
    public void ReturnToRampDuration_PreservesGapsAndCoalescesOverlaps()
    {
        var baseline = CreateSource(includeCompletionDetails: false);
        var now = baseline.WorkOrder.ScheduledArrivalUtc;
        var serviceLines = new[]
        {
            new WorkOrderServiceLineDto(
                Guid.NewGuid(), Guid.NewGuid(), "First return", PerformedBy(Guid.NewGuid(), "Alex"),
                now, now.AddMinutes(10), null, true),
            new WorkOrderServiceLineDto(
                Guid.NewGuid(), Guid.NewGuid(), "Second return", PerformedBy(Guid.NewGuid(), "Sam"),
                now.AddMinutes(60), now.AddMinutes(70), null, true),
            new WorkOrderServiceLineDto(
                Guid.NewGuid(), Guid.NewGuid(), "Overlapping return", PerformedBy(Guid.NewGuid(), "Dana"),
                now.AddMinutes(65), now.AddMinutes(80), null, true)
        };
        var workOrder = baseline.WorkOrder with { ServiceLines = serviceLines };

        WorkOrderPrintDocumentFactory.CalculateReturnToRampDuration(workOrder)
            .ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Create_HandlesMoreStaffAndTasksThanTheOriginalStaticRows()
    {
        var baseline = CreateSource(includeCompletionDetails: true);
        var now = baseline.WorkOrder.ScheduledArrivalUtc;
        var additionalStaff = Enumerable.Range(1, 8)
            .Select(index => new WorkOrderPrintStaffDto(Guid.NewGuid(), $"Manpower Type {index}"))
            .ToList();
        var serviceLines = additionalStaff.Select((staff, index) => new WorkOrderServiceLineDto(
                Guid.NewGuid(), Guid.NewGuid(), $"Overflow Service {index + 1}",
                PerformedBy(staff.StaffMemberId, $"Overflow Staff Member {index + 1}"),
                now.AddMinutes(index), now.AddMinutes(index + 10),
                index == 0
                    ? string.Join(
                        " ",
                        Enumerable.Repeat(
                            "Detailed operational narrative remains readable when a performed service continues across lines.",
                            16))
                    : null,
                index is 0 or 7))
            .ToList();
        var tasks = Enumerable.Range(0, 16)
            .Select(index => baseline.WorkOrder.Tasks[index % baseline.WorkOrder.Tasks.Count] with
            {
                Id = Guid.NewGuid(),
                Description = $"Task {index + 1} with a description that must not hide later tasks"
            })
            .ToList();
        var source = baseline with
        {
            WorkOrder = baseline.WorkOrder with { ServiceLines = serviceLines, Tasks = tasks },
            Staff = baseline.Staff.Concat(additionalStaff).ToList()
        };

        var file = WorkOrderPrintDocumentFactory.Create(source);

        Encoding.ASCII.GetString(file.Content, 0, 4).ShouldBe("%PDF");
        using var stream = new MemoryStream(file.Content, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBeGreaterThan(2);
    }

    private static ApprovedWorkOrderPrintDto CreateSource(bool includeCompletionDetails)
    {
        var now = new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);
        var staffId = Guid.NewGuid();
        var secondStaffId = Guid.NewGuid();
        var thirdStaffId = Guid.NewGuid();
        var signature = includeCompletionDetails
            ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
            : null;

        var workOrder = new WorkOrderDetailDto(
            Guid.NewGuid(), Guid.NewGuid(), "Completion", "Approved", false, null,
            Guid.NewGuid(), "Alex Technician", Guid.NewGuid(), "RJ", "Royal Jordanian",
            Guid.NewGuid(), "HOF", "Al-Ahsa", Guid.NewGuid(), "Extra", "123", now,
            now.AddHours(2), "123", includeCompletionDetails ? Guid.NewGuid() : null,
            includeCompletionDetails ? "A320-200" : null,
            includeCompletionDetails ? "JY-ABC" : null,
            includeCompletionDetails ? now.AddMinutes(8) : null,
            includeCompletionDetails ? now.AddHours(1).AddMinutes(55) : null,
            null, null, "Completed without outstanding defects.",
            signature is null ? null : new WorkOrderSignatureDto("customer.png", "image/png", signature.Length, now.AddHours(2)),
            42, "HOF-0042", Guid.NewGuid(), now.AddHours(2),
            includeCompletionDetails
                ? [
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "Headset", PerformedBy(staffId, "Alex Technician", "EMP-100"),
                        now.AddMinutes(5), now.AddMinutes(35), "Pushback support", false),
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "Transit", PerformedBy(secondStaffId, "Sam Technician", "EMP-200"),
                        now.AddMinutes(15), now.AddMinutes(45), null, false),
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "Daily", PerformedBy(thirdStaffId, "Dana Engineer", "EMP-300"),
                        now.AddMinutes(20), now.AddMinutes(50), null, false),
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "Weekly", PerformedBy(staffId, "Alex Technician", "EMP-100"),
                        now.AddMinutes(40), now.AddMinutes(70), null, false),
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "On Call", PerformedBy(secondStaffId, "Sam Technician", "EMP-200"),
                        now.AddMinutes(60), now.AddMinutes(90), null, false),
                    new WorkOrderServiceLineDto(
                        Guid.NewGuid(), Guid.NewGuid(), "Return Ramp", PerformedBy(thirdStaffId, "Dana Engineer", "EMP-300"),
                        now.AddMinutes(80), now.AddMinutes(100), null, true)
                ]
                : [],
            includeCompletionDetails
                ? [
                    new WorkOrderTaskDto(
                        Guid.NewGuid(), "Major", "Completed inspection", now.AddMinutes(10), now.AddMinutes(50),
                        [new WorkOrderTaskEmployeeDto(staffId, "Alex Technician", "EMP-100")], [],
                        [new WorkOrderTaskMaterialDto(Guid.NewGuid(), "Hydraulic fluid", 2)], [], [], false),
                    new WorkOrderTaskDto(
                        Guid.NewGuid(), "Minor", "Completed follow-up", now.AddMinutes(55), now.AddMinutes(95),
                        [
                            new WorkOrderTaskEmployeeDto(thirdStaffId, "Dana Engineer", "EMP-300"),
                            new WorkOrderTaskEmployeeDto(secondStaffId, "Sam Technician", "EMP-200")
                        ], [], [], [], [], false)
                ]
                : [],
            now.AddHours(-1), now.AddHours(2), Convert.ToBase64String([1, 2, 3]));

        return new ApprovedWorkOrderPrintDto(
            workOrder,
            new WorkOrderPrintFlightDto(
                CurrentFlightNumber: "123",
                OriginalFlightNumber: "122",
                IsPerLanding: false,
                ScheduledAircraftManufacturer: "Airbus",
                ScheduledAircraftModel: "A320",
                PlannedServices:
                [
                    new PlannedServiceDto(Guid.NewGuid(), "Engineer On Call", false),
                    new PlannedServiceDto(Guid.NewGuid(), "Cabin Check", false)
                ],
                AssignedEmployees:
                [
                    new AssignedEmployeeDto(staffId, "Alex Technician", "EMP-100"),
                    new AssignedEmployeeDto(secondStaffId, "Sam Technician", "EMP-200")
                ]),
            includeCompletionDetails ? "Airbus" : null,
            "C-7788",
            Staff: includeCompletionDetails
                ? [
                    new WorkOrderPrintStaffDto(staffId, "Technician"),
                    new WorkOrderPrintStaffDto(secondStaffId, "Technician"),
                    new WorkOrderPrintStaffDto(thirdStaffId, "Engineer")
                ]
                : [],
            signature,
            signature is null ? null : "image/png");
    }

    private static IReadOnlyList<WorkOrderServiceLinePerformerDto> PerformedBy(
        Guid staffMemberId,
        string fullName,
        string employeeId = "EMP") =>
        [new WorkOrderServiceLinePerformerDto(staffMemberId, fullName, employeeId)];
}
