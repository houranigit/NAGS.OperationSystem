using System.Text;
using ClosedXML.Excel;
using MasterData.Contracts.Resources;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class FlightExportDocumentFactoryTests
{
    private static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 7, 23, 19, 45, 0, TimeSpan.Zero);

    private static readonly FlightExportCriteria Criteria = new(
        Search: null,
        StationIds: [],
        CustomerIds: [],
        OperationTypeId: null,
        Statuses: null,
        FromUtc: new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
        ToUtc: new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero),
        ServiceCategories: null,
        ServiceIds: [],
        ToUtcExclusive: false,
        Sort: null);

    [Theory]
    [InlineData("xlsx", "Xlsx")]
    [InlineData("excel", "Xlsx")]
    [InlineData("csv", "Csv")]
    [InlineData("pdf", "Pdf")]
    public void TryParseFormat_AcceptsSupportedTypes(string value, string expected)
    {
        FlightExportDocumentFactory.TryParseFormat(value, out var actual).ShouldBeTrue();
        actual.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("Asia/Riyadh", 180)]
    [InlineData("Arab Standard Time", 180)]
    [InlineData("Browser UTC-05:30", -330)]
    public void ExportTimeZoneResolver_AcceptsSupportedIdentifiers(string id, int expectedOffsetMinutes)
    {
        FlightExportTimeZoneResolver.TryResolve(id, out var timeZone).ShouldBeTrue();

        timeZone.GetUtcOffset(GeneratedAtUtc).TotalMinutes.ShouldBe(expectedOffsetMinutes);
    }

    [Fact]
    public void ExportTimeZoneResolver_OmittedDefaultsToRiyadhAndRejectsInvalidIdentifiers()
    {
        FlightExportTimeZoneResolver.TryResolve(null, out var fallback).ShouldBeTrue();
        fallback.GetUtcOffset(GeneratedAtUtc).ShouldBe(TimeSpan.FromHours(3));

        FlightExportTimeZoneResolver.TryResolve("Browser UTC+14:30", out _).ShouldBeFalse();
        FlightExportTimeZoneResolver.TryResolve("Not/A-Time-Zone", out _).ShouldBeFalse();
    }

    [Fact]
    public void CreateWorkbook_UsesCanonicalReportColumnsIncludingWorkOrderResources()
    {
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.FileName.ShouldStartWith("flights-report-");
        file.FileName.ShouldEndWith(".xlsx");

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Flights");

        sheet.Cell(5, 20).GetString().ShouldBe("Planned Services");
        sheet.Cell(5, 21).GetString().ShouldBe("Services");
        sheet.Cell(5, 22).GetString().ShouldBe("Tools");
        sheet.Cell(5, 23).GetString().ShouldBe("Materials");
        sheet.Cell(5, 24).GetString().ShouldBe("General Support");
        sheet.Cell(5, 25).GetString().ShouldBe("Assigned Employees");
        sheet.Cell(5, 26).GetString().ShouldBe("Remarks");
        sheet.Cell(5, 27).GetString().ShouldBe("Status");
        sheet.Cell(5, 28).GetString().ShouldBe("Tasks");
        sheet.Cell(5, 29).IsEmpty().ShouldBeTrue();

        sheet.Cell(6, 3).GetString().ShouldBe("RJ-707");
        sheet.Cell(6, 14).GetString().ShouldBe("Royal Jordanian");
        sheet.Cell(6, 21).GetString().ShouldBe("Baggage, Transit");
        sheet.Cell(6, 22).GetString().ShouldBe("Towbar");
        sheet.Cell(6, 23).GetString().ShouldBe("Hydraulic fluid");
        sheet.Cell(6, 24).GetString().ShouldBe("GPU");
        sheet.Cell(6, 27).GetString().ShouldBe("In progress");
        sheet.Cell(6, 28).GetString().ShouldBe("Major: Inspect landing gear, Minor: Power aircraft systems");
    }

    [Fact]
    public void CreateWorkbook_AddsOrderedServiceAndTaskDetailSheetsWithOneActivityPerRow()
    {
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [CreateDetailedRow()],
            Criteria,
            GeneratedAtUtc);
        WriteQaSampleWhenRequested(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        workbook.Worksheets.Select(sheet => sheet.Name)
            .ShouldBe(["Flights", "Service Details", "Task Details"]);

        var services = workbook.Worksheet("Service Details");
        services.Cell(5, 20).GetString().ShouldBe("Service");
        services.Cell(5, 23).GetString().ShouldBe("Performed By");
        services.Cell(6, 2).GetString().ShouldBe("AMM-0042");
        services.Cell(6, 4).GetString().ShouldBe("RJ-707");
        services.Cell(6, 20).GetString().ShouldBe("Baggage handling");
        services.Cell(6, 23).GetString().ShouldBe("Alex Engineer");
        services.Cell(6, 16).GetString().ShouldBe("Work order");
        services.Cell(7, 20).GetString().ShouldBe("Aircraft inspection");
        services.Cell(7, 16).GetString().ShouldBe("Return to ramp #1");
        services.Cell(7, 19).GetString().ShouldBe("Bird-strike inspection");
        services.Cell(6, 12).DataType.ShouldBe(XLDataType.DateTime);
        services.Cell(6, 21).DataType.ShouldBe(XLDataType.DateTime);
        services.Cell(7, 17).DataType.ShouldBe(XLDataType.DateTime);
        services.AutoFilter.IsEnabled.ShouldBeTrue();

        var tasks = workbook.Worksheet("Task Details");
        tasks.Cell(5, 20).GetString().ShouldBe("Major/Minor");
        tasks.Cell(5, 25).GetString().ShouldBe("Tools");
        tasks.Cell(5, 27).GetString().ShouldBe("General Support");
        tasks.Cell(6, 2).GetString().ShouldBe("AMM-0042");
        tasks.Cell(6, 4).GetString().ShouldBe("RJ-707");
        tasks.Cell(6, 20).GetString().ShouldBe("Major");
        tasks.Cell(6, 21).GetString().ShouldBe("Inspect landing gear");
        tasks.Cell(6, 24).GetString().ShouldBe("Alex Engineer");
        tasks.Cell(6, 25).GetString().ShouldBe("Towbar × 2");
        tasks.Cell(6, 26).GetString().ShouldBe("Hydraulic fluid × 1.5");
        tasks.Cell(7, 16).GetString().ShouldBe("Return to ramp #1");
        tasks.Cell(7, 27).GetString().ShouldBe("GPU: 2026-07-23 10:20 UTC → open");
        tasks.Cell(6, 22).DataType.ShouldBe(XLDataType.DateTime);
        tasks.Cell(7, 17).DataType.ShouldBe(XLDataType.DateTime);
        tasks.AutoFilter.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void CreateWorkbook_DetailSheetsProtectAllUserTextFromFormulaInjection()
    {
        var maliciousReturn = new FlightExportReturnToRampContextDto(
            Guid.NewGuid(),
            1,
            GeneratedAtUtc.AddMinutes(1),
            GeneratedAtUtc.AddMinutes(20),
            "+RTR description");
        var source = CreateDetailedRow();
        var row = source with
        {
            CustomerIataCode = null,
            CustomerName = "=Customer",
            StationIata = "@Station",
            StationName = "+Station name",
            OperationTypeName = "-Operation",
            ApprovedWorkOrder = source.ApprovedWorkOrder! with
            {
                ApprovalNumber = "=WO",
                ActualFlightNumber = "@Actual flight",
                WorkOrderStatus = "+Approved",
                ServiceDetails =
                [
                    new FlightExportServiceDetailDto(
                        Guid.NewGuid(),
                        "=Service",
                        GeneratedAtUtc,
                        GeneratedAtUtc.AddMinutes(5),
                        ["@Engineer"],
                        "-Service description",
                        maliciousReturn)
                ],
                TaskDetails =
                [
                    new FlightExportTaskDetailDto(
                        Guid.NewGuid(),
                        "=Major",
                        "+Task description",
                        GeneratedAtUtc,
                        GeneratedAtUtc.AddMinutes(5),
                        ["@Engineer"],
                        [QuantityResource("=Tool", 2)],
                        [QuantityResource("+Material", 1)],
                        [QuantityResource("@Support", 1)],
                        maliciousReturn)
                ]
            }
        };

        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [row],
            Criteria,
            GeneratedAtUtc);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var services = workbook.Worksheet("Service Details");
        var tasks = workbook.Worksheet("Task Details");
        var dangerousServiceCells = new[] { 2, 3, 5, 8, 9, 10, 11, 19, 20, 23, 24 }
            .Select(column => services.Cell(6, column));
        var dangerousTaskCells = new[] { 2, 3, 5, 8, 9, 10, 11, 19, 20, 21, 24, 25, 26, 27 }
            .Select(column => tasks.Cell(6, column));

        dangerousServiceCells.Concat(dangerousTaskCells).ShouldAllBe(cell =>
            !cell.HasFormula && cell.Style.IncludeQuotePrefix);
        services.Cell(6, 20).GetString().ShouldBe("=Service");
        services.Cell(6, 24).GetString().ShouldBe("-Service description");
        tasks.Cell(6, 25).GetString().ShouldBe("=Tool × 2");
        tasks.Cell(6, 26).GetString().ShouldBe("+Material × 1");
        tasks.Cell(6, 27).GetString().ShouldBe("@Support × 1");
    }

    [Fact]
    public void CreateWorkbook_KeepsPolishedEmptyDetailSheetsWhenNoActivitiesExist()
    {
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var services = workbook.Worksheet("Service Details");
        var tasks = workbook.Worksheet("Task Details");

        services.Cell(5, 20).GetString().ShouldBe("Service");
        services.Cell(6, 1).GetString().ShouldBe("No work-order services match the selected flights.");
        services.AutoFilter.IsEnabled.ShouldBeTrue();
        tasks.Cell(5, 20).GetString().ShouldBe("Major/Minor");
        tasks.Cell(6, 1).GetString().ShouldBe("No work-order tasks match the selected flights.");
        tasks.AutoFilter.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void CreateCsv_UsesCanonicalHeadersAndProtectsEveryResourceColumn()
    {
        var row = CreateRow() with
        {
            ApprovedWorkOrder = CreateRow().ApprovedWorkOrder! with
            {
                ToolNames = ["@Towbar"],
                MaterialNames = ["+Hydraulic fluid"],
                GeneralSupportNames = ["=GPU"],
                TaskNames = ["-Unsafe task"]
            }
        };

        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Csv,
            [row],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("text/csv; charset=utf-8");
        file.Content.Take(3).ShouldBe([(byte)0xEF, (byte)0xBB, (byte)0xBF]);

        var csv = Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3);
        csv.Split('\n', 2)[0].TrimEnd('\r').ShouldBe(
            "#,WO#,Flight#,WO Flight#,STA,STD,ATA,ATD,Arrival Delay,Departure Delay,Scheduled Duration,Actual Duration,Customer IATA Code,Customer Name,Station IATA Code,Station Name,Aircraft Manufacturer,Aircraft Model,Aircraft Tail Number,Planned Services,Services,Tools,Materials,General Support,Assigned Employees,Remarks,Status,Tasks");
        csv.ShouldContain(",'@Towbar,");
        csv.ShouldContain(",'+Hydraulic fluid,");
        csv.ShouldContain(",'=GPU,");
        csv.ShouldContain(",'-Unsafe task");
    }

    [Fact]
    public void CreateWorkbook_ProtectsEveryResourceColumnFromFormulaInjection()
    {
        var row = CreateRow() with
        {
            ApprovedWorkOrder = CreateRow().ApprovedWorkOrder! with
            {
                ToolNames = ["@Towbar"],
                MaterialNames = ["+Hydraulic fluid"],
                GeneralSupportNames = ["=GPU"],
                TaskNames = ["-Unsafe task"]
            }
        };

        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [row],
            Criteria,
            GeneratedAtUtc);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Flights");
        sheet.Cell(6, 22).GetString().ShouldBe("@Towbar");
        sheet.Cell(6, 23).GetString().ShouldBe("+Hydraulic fluid");
        sheet.Cell(6, 24).GetString().ShouldBe("=GPU");
        sheet.Range("V6:X6").Cells().ShouldAllBe(cell =>
            !cell.HasFormula && cell.Style.IncludeQuotePrefix);
        sheet.Cell(6, 28).GetString().ShouldBe("-Unsafe task");
        sheet.Cell(6, 28).HasFormula.ShouldBeFalse();
        sheet.Cell(6, 28).Style.IncludeQuotePrefix.ShouldBeTrue();
    }

    [Fact]
    public void CreateFormats_DefaultToUtcWithoutChangingStoredInstants()
    {
        var xlsx = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [CreateDetailedRow()],
            Criteria,
            GeneratedAtUtc);

        using (var stream = new MemoryStream(xlsx.Content))
        using (var workbook = new XLWorkbook(stream))
        {
            var flights = workbook.Worksheet("Flights");
            flights.Cell(6, 5).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 9, 30, 0));
            flights.Column(5).Style.DateFormat.Format.ShouldContain("UTC");
            flights.Cell(2, 1).GetString().ShouldContain("Generated 2026-07-23 19:45 UTC");
        }

        var csv = FlightExportDocumentFactory.Create(
            FlightExportFormat.Csv,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);
        Encoding.UTF8.GetString(csv.Content, 3, csv.Content.Length - 3)
            .ShouldContain("2026-07-23T09:30:00Z");
        FlightExportDocumentFactory.FormatReportTimestamp(GeneratedAtUtc, TimeZoneInfo.Utc)
            .ShouldBe("2026-07-23 19:45 UTC");
        FlightExportDocumentFactory.PdfDateScope(Criteria, TimeZoneInfo.Utc)
            .ShouldContain("23 Jul 2026 00:00 UTC");
    }

    [Fact]
    public void CreateFormats_ConvertTimestampsAndScopeToRiyadhWithExplicitZoneLabels()
    {
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var localDayCriteria = Criteria with
        {
            FromUtc = new DateTimeOffset(2026, 8, 7, 21, 0, 0, TimeSpan.Zero),
            ToUtc = new DateTimeOffset(2026, 8, 8, 20, 59, 59, TimeSpan.Zero)
        };
        var xlsx = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx,
            [CreateDetailedRow()],
            localDayCriteria,
            GeneratedAtUtc,
            riyadh);
        WriteQaSampleWhenRequested(xlsx.Content, "FLIGHT_EXPORT_RIYADH_SAMPLE_PATH");

        using (var stream = new MemoryStream(xlsx.Content))
        using (var workbook = new XLWorkbook(stream))
        {
            workbook.Worksheets.Select(sheet => sheet.Name)
                .ShouldBe(["Flights", "Service Details", "Task Details"]);
            var flights = workbook.Worksheet("Flights");
            flights.Cell(6, 5).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 12, 30, 0));
            flights.Column(5).Style.DateFormat.Format.ShouldContain("Asia/Riyadh");
            flights.Cell(2, 1).GetString()
                .ShouldContain("Generated 2026-07-23 22:45 +03:00 [Asia/Riyadh]");
            flights.Cell(3, 1).GetString()
                .ShouldContain("Scheduled arrival: 2026-08-08 to 2026-08-08 [Asia/Riyadh]");

            var services = workbook.Worksheet("Service Details");
            services.Cell(6, 21).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 12, 40, 0));
            var tasks = workbook.Worksheet("Task Details");
            tasks.Cell(7, 27).GetString().ShouldBe(
                "GPU: 2026-07-23 13:20 +03:00 → open [Asia/Riyadh]");
        }

        var csv = FlightExportDocumentFactory.Create(
            FlightExportFormat.Csv,
            [CreateRow()],
            localDayCriteria,
            GeneratedAtUtc,
            riyadh);
        Encoding.UTF8.GetString(csv.Content, 3, csv.Content.Length - 3)
            .ShouldContain("2026-07-23T12:30:00+03:00 [Asia/Riyadh]");
        FlightExportDocumentFactory.FormatReportTimestamp(GeneratedAtUtc, riyadh)
            .ShouldBe("2026-07-23 22:45 +03:00 [Asia/Riyadh]");
        FlightExportDocumentFactory.PdfDateScope(localDayCriteria, riyadh)
            .ShouldContain("08 Aug 2026 00:00 +03:00");
        var pdf = FlightExportDocumentFactory.Create(
            FlightExportFormat.Pdf,
            [CreateRow()],
            localDayCriteria,
            GeneratedAtUtc,
            riyadh);
        Encoding.ASCII.GetString(pdf.Content, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void CreatePdf_RemainsTheCanonicalNativeDailyOperationReport()
    {
        FlightExportDocumentFactory.BuildPdfColumns(Criteria)
            .Select(column => column.Header)
            .ShouldBe(["#", "WO#", "Flight#", "Customer", "Station", "Aircraft", "Services", "Remarks"]);

        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Pdf,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldStartWith("flights-report-");
        file.FileName.ShouldEndWith(".pdf");
        Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-");
    }

    private static FlightExportRowDto CreateRow() => new(
        Guid.Parse("7a4d568c-85aa-4175-a32f-fec41dc05d61"),
        "707",
        "707",
        "RJ",
        "Royal Jordanian",
        "AMM",
        "Queen Alia International Airport",
        "Arrival",
        new DateTimeOffset(2026, 7, 23, 9, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 23, 11, 0, 0, TimeSpan.Zero),
        "InProgress",
        false,
        ["Marshalling"],
        ["Alex Engineer"],
        new ApprovedWorkOrderExportDto(
            "AMM-0042",
            "708",
            new DateTimeOffset(2026, 7, 23, 9, 35, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 23, 10, 55, 0, TimeSpan.Zero),
            "Airbus",
            "A320",
            "JY-ABC",
            ["Baggage", "Transit"],
            ["Towbar"],
            ["Hydraulic fluid"],
            ["GPU"],
            "Completed without defects")
        {
            TaskNames = ["Major: Inspect landing gear", "Minor: Power aircraft systems"]
        });

    private static FlightExportRowDto CreateDetailedRow()
    {
        var row = CreateRow();
        var returnToRamp = new FlightExportReturnToRampContextDto(
            Guid.Parse("f59bb3bd-54c3-4550-9b6e-a5e4b19ad8a3"),
            1,
            new DateTimeOffset(2026, 7, 23, 10, 10, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 23, 10, 45, 0, TimeSpan.Zero),
            "Bird-strike inspection");
        return row with
        {
            ApprovedWorkOrder = row.ApprovedWorkOrder! with
            {
                WorkOrderId = Guid.Parse("d4133fb9-1531-45d3-a52b-5862287c7afe"),
                WorkOrderStatus = "Approved",
                ServiceDetails =
                [
                    new FlightExportServiceDetailDto(
                        Guid.Parse("bf0c48f1-6642-405b-97e3-d852b37b4d3f"),
                        "Baggage handling",
                        new DateTimeOffset(2026, 7, 23, 9, 40, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero),
                        ["Alex Engineer"],
                        "Completed at stand A12",
                        null),
                    new FlightExportServiceDetailDto(
                        Guid.Parse("2e8d4f30-a043-497c-b442-a11e26035210"),
                        "Aircraft inspection",
                        new DateTimeOffset(2026, 7, 23, 10, 15, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 7, 23, 10, 35, 0, TimeSpan.Zero),
                        ["Ramp Engineer"],
                        "No structural damage found",
                        returnToRamp)
                ],
                TaskDetails =
                [
                    new FlightExportTaskDetailDto(
                        Guid.Parse("d4ac055b-548b-40f2-8a02-429894e892a1"),
                        "Major",
                        "Inspect landing gear",
                        new DateTimeOffset(2026, 7, 23, 9, 45, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 7, 23, 10, 5, 0, TimeSpan.Zero),
                        ["Alex Engineer"],
                        [QuantityResource("Towbar", 2)],
                        [QuantityResource("Hydraulic fluid", 1.5m)],
                        [],
                        null),
                    new FlightExportTaskDetailDto(
                        Guid.Parse("b4040489-d655-4382-bec3-83c8e4d88900"),
                        "Minor",
                        "Power aircraft systems",
                        new DateTimeOffset(2026, 7, 23, 10, 20, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 7, 23, 10, 40, 0, TimeSpan.Zero),
                        ["Ramp Engineer"],
                        [],
                        [],
                        [DurationResource(
                            "GPU",
                            new DateTimeOffset(2026, 7, 23, 10, 20, 0, TimeSpan.Zero),
                            null)],
                        returnToRamp)
                ]
            }
        };
    }

    private static void WriteQaSampleWhenRequested(byte[] content)
        => WriteQaSampleWhenRequested(content, "FLIGHT_EXPORT_SAMPLE_PATH");

    private static void WriteQaSampleWhenRequested(byte[] content, string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllBytes(path, content);
    }

    private static FlightExportResourceUsageDto QuantityResource(string name, decimal quantity) =>
        new(name, ResourceCalculationType.Quantity, quantity, null, null);

    private static FlightExportResourceUsageDto DurationResource(
        string name,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc) =>
        new(name, ResourceCalculationType.Duration, null, fromUtc, toUtc);
}
