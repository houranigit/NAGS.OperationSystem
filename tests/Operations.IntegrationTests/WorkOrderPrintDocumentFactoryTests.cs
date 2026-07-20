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
    public void Create_PreservesHistoricPageAndReturnsNamedPdf()
    {
        var source = CreateSource(includeCompletionDetails: true);

        var file = WorkOrderPrintDocumentFactory.Create(source);

        file.FileName.ShouldBe("work-order-HOF-0042.pdf");
        file.Content.Length.ShouldBeGreaterThan(100_000);
        Encoding.ASCII.GetString(file.Content, 0, 4).ShouldBe("%PDF");

        using var stream = new MemoryStream(file.Content, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBe(1);
        document.Pages[0].Width.Point.ShouldBe(595.676, tolerance: 0.01);
        document.Pages[0].Height.Point.ShouldBe(879.144, tolerance: 0.01);
        var content = ContentReader.ReadContent(document.Pages[0]);
        content.OfType<COperator>()
            .Count(operation => operation.Name is "Tj" or "TJ")
            .ShouldBeGreaterThan(10);
    }

    [Fact]
    public void Create_ToleratesApprovedPerLandingWorkOrderWithoutActualsAircraftOrSignature()
    {
        var baseline = CreateSource(includeCompletionDetails: false);
        var source = baseline with
        {
            WorkOrder = baseline.WorkOrder with
            {
                Remarks = $"Long unbroken value: {new string('X', 2_000)}"
            }
        };

        var file = WorkOrderPrintDocumentFactory.Create(source);

        Encoding.ASCII.GetString(file.Content, 0, 4).ShouldBe("%PDF");
        file.FileName.ShouldBe("work-order-HOF-0042.pdf");
    }

    [Fact]
    public void MergeWorkerWindows_PreservesGapsAndCoalescesOverlaps()
    {
        var start = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        var source = new[]
        {
            new WorkOrderPrintDocumentFactory.WorkerWindow("Alex", start, start.AddMinutes(15)),
            new WorkOrderPrintDocumentFactory.WorkerWindow("Alex", start.AddHours(1), start.AddHours(1).AddMinutes(15)),
            new WorkOrderPrintDocumentFactory.WorkerWindow("Alex", start.AddHours(1).AddMinutes(10), start.AddHours(1).AddMinutes(30))
        };

        var merged = WorkOrderPrintDocumentFactory.MergeWorkerWindows(source);

        merged.Count.ShouldBe(2);
        merged.Aggregate(TimeSpan.Zero, (total, window) => total + (window.ToUtc - window.FromUtc))
            .ShouldBe(TimeSpan.FromMinutes(45));
    }

    private static ApprovedWorkOrderPrintDto CreateSource(bool includeCompletionDetails)
    {
        var now = new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);
        var staffId = Guid.NewGuid();
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
                ? [new WorkOrderServiceLineDto(
                    Guid.NewGuid(), Guid.NewGuid(), "Headset", staffId, "Alex Technician",
                    now.AddMinutes(5), now.AddMinutes(35), "Pushback support", false)]
                : [],
            includeCompletionDetails
                ? [new WorkOrderTaskDto(
                    Guid.NewGuid(), "Major", "Completed inspection", now.AddMinutes(10), now.AddMinutes(50),
                    [new WorkOrderTaskEmployeeDto(staffId, "Alex Technician", "EMP-100")], [],
                    [new WorkOrderTaskMaterialDto(Guid.NewGuid(), "Hydraulic fluid", 2)], [], [], false)]
                : [],
            now.AddHours(-1), now.AddHours(2), Convert.ToBase64String([1, 2, 3]));

        return new ApprovedWorkOrderPrintDto(
            workOrder,
            includeCompletionDetails ? "Airbus" : null,
            "C-7788",
            ["Headset"],
            IsPerLanding: !includeCompletionDetails,
            IsOnCall: false,
            signature,
            signature is null ? null : "image/png");
    }
}
