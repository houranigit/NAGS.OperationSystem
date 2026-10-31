using ClosedXML.Excel;
using MasterData.Contracts.Resources;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class BillingExportDocumentFactoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 2, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BillingReport_RepeatsWorkOrderDetailsForEveryRecordedItemAndManpowerEntry()
    {
        var file = BillingExportDocumentFactory.Create([CreateRow()], Start, TimeZoneInfo.Utc);
        var samplePath = Environment.GetEnvironmentVariable("BILLING_EXPORT_SAMPLE_PATH");
        if (!string.IsNullOrWhiteSpace(samplePath))
            File.WriteAllBytes(samplePath, file.Content);
        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.FileName.ShouldStartWith("billing-report-");
        file.FileName.ShouldEndWith(".xlsx");

        using var workbook = Open(file);
        workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(["Billing Report"]);
        var sheet = workbook.Worksheet(1);
        sheet.Range(1, 1, 1, 20).Cells().Select(cell => cell.GetString()).ShouldBe(
        [
            "WO ID", "Customer", "Station", "AC TYPE", "AC REG. NO", "Flight No",
            "STA", "STA", "ATA", "ATA", "STD", "STD", "ATD", "ATD",
            "Area", "Description", "Start Date", "Start Time", "End Date", "End Time"
        ]);
        sheet.LastColumnUsed()!.ColumnNumber().ShouldBe(20);
        // 2 services + 2 tools + 1 material + 3 support usages + 2 manpower entries.
        sheet.LastRowUsed()!.RowNumber().ShouldBe(11);
        sheet.Range(2, 15, 11, 15).Cells().Select(cell => cell.GetString()).ShouldBe(
            ["Service", "Service", "Tool", "Tool", "Material", "General Support", "General Support", "General Support", "Manpower", "Manpower"]);
        for (var row = 2; row <= 11; row++)
        {
            sheet.Cell(row, 1).GetString().ShouldBe("JED-00042");
            sheet.Cell(row, 2).GetString().ShouldBe("Example Airways");
            sheet.Cell(row, 3).GetString().ShouldBe("Jeddah");
            sheet.Cell(row, 4).GetString().ShouldBe("Airbus A320-NEO");
            sheet.Cell(row, 5).GetString().ShouldBe("HZ-123");
            sheet.Cell(row, 6).GetString().ShouldBe("00123");
            sheet.Cell(row, 6).DataType.ShouldBe(XLDataType.Text);
        }
        sheet.Cell(4, 16).GetString().ShouldBe(sheet.Cell(5, 16).GetString());
        sheet.Cell(10, 16).GetString().ShouldBe("Technican / Mechanic");
        sheet.Cell(11, 16).GetString().ShouldBe("Technican / Mechanic");
        sheet.Cell(10, 18).GetTimeSpan().ShouldBe(Start.AddMinutes(5).TimeOfDay);
        sheet.Cell(11, 18).GetTimeSpan().ShouldBe(Start.AddMinutes(10).TimeOfDay);
        sheet.AutoFilter.IsEnabled.ShouldBeTrue();
        sheet.SheetView.SplitRow.ShouldBe(1);
        sheet.MergedRanges.ShouldBeEmpty();
    }

    [Fact]
    public void BillingReport_SplitsTypedLocalDatesAnd24HourTimesAndKeepsMissingTimesBlank()
    {
        using var workbook = Open(BillingExportDocumentFactory.Create(
            [CreateRow()], Start, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(2, 7).GetDateTime().ShouldBe(new DateTime(2026, 10, 29));
        sheet.Cell(2, 8).GetTimeSpan().ShouldBe(new TimeSpan(21, 15, 0));
        sheet.Cell(2, 9).GetDateTime().ShouldBe(new DateTime(2026, 10, 29));
        sheet.Cell(2, 10).GetTimeSpan().ShouldBe(new TimeSpan(21, 25, 0));
        sheet.Cell(2, 11).GetDateTime().ShouldBe(new DateTime(2026, 10, 30));
        sheet.Cell(2, 12).GetTimeSpan().ShouldBe(new TimeSpan(1, 15, 0));
        sheet.Cell(2, 13).IsEmpty().ShouldBeTrue();
        sheet.Cell(2, 14).IsEmpty().ShouldBeTrue();
        sheet.Cell(2, 17).GetDateTime().ShouldBe(new DateTime(2026, 10, 29));
        sheet.Cell(2, 18).GetTimeSpan().ShouldBe(new TimeSpan(21, 15, 0));
        sheet.Cell(2, 7).Style.NumberFormat.Format.ShouldBe("yyyy/mm/dd");
        sheet.Cell(2, 8).Style.NumberFormat.Format.ShouldBe("hh:mm");
        sheet.Range(6, 17, 6, 20).Cells().All(cell => cell.IsEmpty()).ShouldBeTrue();
        sheet.Cell(7, 17).IsEmpty().ShouldBeFalse();
        sheet.Cell(7, 19).IsEmpty().ShouldBeTrue();
        sheet.Cell(7, 20).IsEmpty().ShouldBeTrue();
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(7, 1)]
    public void BillingReport_ConvertsEachTimestampAcrossDaylightSavingTransition(int utcHour, int localHour)
    {
        var row = CreateRow() with { ScheduledArrivalUtc = new DateTimeOffset(2026, 11, 1, utcHour, 30, 0, TimeSpan.Zero) };
        using var workbook = Open(BillingExportDocumentFactory.Create(
            [row], Start, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")));
        workbook.Worksheet(1).Cell(2, 8).GetTimeSpan().ShouldBe(new TimeSpan(localHour, 30, 0));
    }

    [Fact]
    public void BillingReport_DoesNotInventRowsFromPlannedServicesOrAssignmentsAndPreservesLiteralText()
    {
        var row = CreateRow();
        using var empty = Open(BillingExportDocumentFactory.Create(
            [row with { ApprovedWorkOrder = null }], Start, TimeZoneInfo.Utc));
        empty.Worksheet(1).LastRowUsed()!.RowNumber().ShouldBe(1);

        row = row with
        {
            CustomerName = "=HYPERLINK(\"https://example.test\")",
            ApprovedWorkOrder = row.ApprovedWorkOrder! with { ApprovalNumber = null }
        };
        using var workbook = Open(BillingExportDocumentFactory.Create([row], Start, TimeZoneInfo.Utc));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(2, 1).IsEmpty().ShouldBeTrue();
        sheet.Cell(2, 2).GetString().ShouldBe(row.CustomerName);
        sheet.Cell(2, 2).HasFormula.ShouldBeFalse();
        sheet.Cell(2, 2).Style.IncludeQuotePrefix.ShouldBeTrue();
    }

    private static XLWorkbook Open(FlightExportFile file) => new(new MemoryStream(file.Content));

    private static FlightExportRowDto CreateRow()
    {
        var rtr = new FlightExportReturnToRampContextDto(Guid.NewGuid(), 1, Start, Start.AddHours(1), "Return to ramp");
        var workOrder = new ApprovedWorkOrderExportDto(
            "JED-00042", "00123", Start.AddMinutes(10), null, "Airbus", "A320-NEO", "HZ-123",
            ["Transit", "Transit"], ["Towbar"], ["Oil"], ["GPU"], null)
        {
            ServiceDetails =
            [
                new(Guid.NewGuid(), "Transit", Start, Start.AddHours(1), [], null, null),
                new(Guid.NewGuid(), "Transit", Start.AddHours(2), Start.AddHours(3), [], null, rtr)
            ],
            TaskDetails =
            [
                new(Guid.NewGuid(), "Minor", "Task should not create its own billing row", Start, Start.AddHours(1), [],
                    [Resource("Towbar"), Resource("Towbar")],
                    [new("Oil", ResourceCalculationType.Quantity, 2, null, null)],
                    [new("GPU", ResourceCalculationType.Duration, null, Start, null), Resource("Air start"), Resource("Steps")], rtr)
            ],
            ManpowerDetails =
            [
                new("Technican / Mechanic", Start.AddMinutes(5), Start.AddMinutes(35)),
                new("Technican / Mechanic", Start.AddMinutes(10), Start.AddMinutes(40))
            ]
        };
        return new FlightExportRowDto(Guid.NewGuid(), "Scheduled Number", "Original Number", "EX",
            "Example Airways", "JED", "Jeddah", "Schedule", Start, Start.AddHours(4), "Completed", false,
            ["Planned only"], ["Assigned only"], workOrder);
    }

    private static FlightExportResourceUsageDto Resource(string name) =>
        new(name, ResourceCalculationType.Duration, null, Start, Start.AddHours(1));
}
