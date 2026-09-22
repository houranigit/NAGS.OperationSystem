using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
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
            .ShouldBe(["Flights", "Service Details", "Task Details", "Flight Breakdown"]);

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
    public void CreateWorkbook_FlightBreakdownPreservesFlightColumnsAndEverySeparateActivity()
    {
        var source = CreateResourceDetailedRow();
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx, [source], Criteria, GeneratedAtUtc);
        WriteQaSampleWhenRequested(file.Content, "FLIGHT_EXPORT_RESOURCES_SAMPLE_PATH");

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var breakdown = workbook.Worksheet("Flight Breakdown");
        var flights = workbook.Worksheet("Flights");
        breakdown.Range(5, 1, 5, 28).Cells().Select(cell => cell.GetString()).ShouldBe(
            flights.Range(5, 1, 5, 28).Cells().Select(cell => cell.GetString()));
        breakdown.Range(5, 29, 5, 44).Cells().Select(cell => cell.GetString()).ShouldBe(
            ["Row Type", "Activity Context", "RTR From", "RTR To", "RTR Description", "Activity Description",
                "Calculation Type", "Quantity", "From", "To", "Duration", "Performed By",
                "Flight ID", "Work Order ID", "Activity ID", "RTR ID"]);
        // 1 planned service + 1 employee + 2 performed services + 3 tasks + 10 usages.
        breakdown.LastRowUsed()!.RowNumber().ShouldBe(22);
        breakdown.Range(6, 1, 22, 1).Cells().Select(cell => cell.GetValue<int>())
            .ShouldBe(Enumerable.Range(1, 17));
        breakdown.Range(6, 29, 22, 29).Cells().Select(cell => cell.GetString()).ShouldBe(
            ["Planned Service", "Assigned Employee", "Service", "Service", "Task",
                "Tool", "Tool", "Tool", "Tool", "Material", "General Support", "Task",
                "Tool", "Material", "General Support", "General Support", "Task"]);
        foreach (var rowNumber in Enumerable.Range(6, 17))
        {
            foreach (var column in Enumerable.Range(2, 18).Concat([26, 27]))
                breakdown.Cell(rowNumber, column).Value.ShouldBe(flights.Cell(6, column).Value);
            breakdown.Cell(rowNumber, 41).GetString().ShouldBe(source.Id.ToString());
            breakdown.Cell(rowNumber, 42).GetString().ShouldBe(source.ApprovedWorkOrder!.WorkOrderId.ToString());
        }

        breakdown.Cell(6, 20).GetString().ShouldBe("Marshalling");
        breakdown.Cell(7, 25).GetString().ShouldBe("Alex Engineer");
        breakdown.Range(8, 21, 9, 21).Cells().Select(cell => cell.GetString())
            .ShouldBe(["Baggage handling", "Aircraft inspection"]);
        breakdown.Cell(10, 28).GetString().ShouldBe("Major: Inspect landing gear");
        breakdown.Range(11, 22, 14, 22).Cells().Select(cell => cell.GetString())
            .ShouldBe(["Towbar", "Jack", "Torque wrench", "Wheel dolly"]);
        breakdown.Cell(15, 23).GetString().ShouldBe("Hydraulic fluid");
        breakdown.Cell(16, 24).GetString().ShouldBe("Passenger stairs");
        breakdown.Cell(18, 22).GetString().ShouldBe("Towbar");
        breakdown.Cell(19, 23).GetString().ShouldBe("Engine oil");
        breakdown.Cell(20, 24).GetString().ShouldBe("GPU");
        breakdown.Cell(21, 24).GetString().ShouldBe("Air starter");
        breakdown.Cell(22, 28).GetString().ShouldBe("Major: Visual check without resources");
        breakdown.Range(10, 30, 16, 30).Cells().ShouldAllBe(cell => cell.GetString() == "Work order");
        breakdown.Range(17, 30, 21, 30).Cells().ShouldAllBe(cell => cell.GetString() == "Return to ramp #1");
        breakdown.Range(17, 33, 21, 33).Cells().ShouldAllBe(cell => cell.GetString() == "Bird-strike inspection");
        breakdown.Range(11, 28, 16, 28).Cells().ShouldAllBe(cell => cell.GetString() == "Major: Inspect landing gear");
        breakdown.Range(18, 28, 21, 28).Cells().ShouldAllBe(cell => cell.GetString() == "Minor: Power aircraft systems");
        breakdown.Cell(11, 43).GetString().ShouldBe(source.ApprovedWorkOrder!.TaskDetails[0].Id.ToString());
        breakdown.Cell(18, 43).GetString().ShouldBe(source.ApprovedWorkOrder.TaskDetails[1].Id.ToString());
        breakdown.Cell(18, 44).GetString().ShouldBe(source.ApprovedWorkOrder.TaskDetails[1].ReturnToRamp!.Id.ToString());
        breakdown.Range(11, 21, 21, 21).Cells().ShouldAllBe(cell => cell.IsEmpty());
        breakdown.Range(8, 22, 9, 25).Cells().ShouldAllBe(cell => cell.IsEmpty());
        breakdown.Cell(11, 36).GetValue<decimal>().ShouldBe(2m);
        breakdown.Cell(18, 36).GetValue<decimal>().ShouldBe(1m);
        breakdown.AutoFilter.IsEnabled.ShouldBeTrue();
        breakdown.AutoFilter.Range.RangeAddress.LastAddress.RowNumber.ShouldBe(22);
        breakdown.AutoFilter.Range.RangeAddress.LastAddress.ColumnNumber.ShouldBe(44);
        breakdown.SheetView.SplitColumn.ShouldBe(2);
        breakdown.SheetView.SplitRow.ShouldBe(5);
    }

    [Fact]
    public void CreateWorkbook_FlightBreakdownUsesNativeQuantitiesDatesAndElapsedDurations()
    {
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx, [CreateResourceDetailedRow()], Criteria, GeneratedAtUtc, riyadh);
        WriteQaSampleWhenRequested(file.Content, "FLIGHT_EXPORT_RESOURCES_RIYADH_SAMPLE_PATH");

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var breakdown = workbook.Worksheet("Flight Breakdown");
        breakdown.Cell(6, 5).DataType.ShouldBe(XLDataType.DateTime);
        breakdown.Cell(6, 9).DataType.ShouldBe(XLDataType.Number);
        breakdown.Cell(15, 35).GetString().ShouldBe("Quantity");
        breakdown.Cell(15, 36).DataType.ShouldBe(XLDataType.Number);
        breakdown.Cell(15, 36).GetValue<decimal>().ShouldBe(1.5m);
        breakdown.Range(15, 37, 15, 39).Cells().ShouldAllBe(cell => cell.IsEmpty());
        breakdown.Cell(19, 36).GetValue<decimal>().ShouldBe(0.75m);
        breakdown.Cell(20, 35).GetString().ShouldBe("Duration");
        breakdown.Cell(20, 36).IsEmpty().ShouldBeTrue();
        breakdown.Cell(20, 37).DataType.ShouldBe(XLDataType.DateTime);
        breakdown.Cell(20, 37).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 13, 20, 0));
        breakdown.Cell(20, 38).DataType.ShouldBe(XLDataType.DateTime);
        breakdown.Cell(20, 38).GetDateTime().ShouldBe(new DateTime(2026, 7, 24, 15, 50, 0));
        // Numeric elapsed days preserve durations above 24 hours for Excel calculations and imports.
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var breakdownXml = archive.GetEntry("xl/worksheets/sheet4.xml")!.Open();
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var durationCell = XDocument.Load(breakdownXml).Descendants(spreadsheet + "c")
            .Single(cell => cell.Attribute("r")!.Value == "AM20");
        (durationCell.Attribute("t")?.Value ?? "n").ShouldBe("n");
        double.Parse(durationCell.Element(spreadsheet + "v")!.Value, CultureInfo.InvariantCulture)
            .ShouldBe(TimeSpan.FromHours(26.5).TotalDays, 0.000000001);
        durationCell.Element(spreadsheet + "f").ShouldBeNull();
        breakdown.Cell(20, 39).Style.NumberFormat.Format.ShouldBe("[h]:mm");
        breakdown.Cell(21, 37).DataType.ShouldBe(XLDataType.DateTime);
        breakdown.Cell(21, 38).IsEmpty().ShouldBeTrue();
        breakdown.Cell(21, 39).GetString().ShouldBe("Open");
        breakdown.Cell(20, 31).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 13, 10, 0));
        breakdown.Cell(8, 37).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 12, 40, 0));
        breakdown.Cell(10, 37).GetDateTime().ShouldBe(new DateTime(2026, 7, 23, 12, 45, 0));
        breakdown.Column(37).Style.DateFormat.Format.ShouldContain("Asia/Riyadh");
        breakdown.Column(38).Style.DateFormat.Format.ShouldContain("Asia/Riyadh");
        breakdown.Cell(2, 1).GetString().ShouldContain("Generated 2026-07-23 22:45 +03:00 [Asia/Riyadh]");
    }

    [Fact]
    public void CreateWorkbook_FlightBreakdownRetainsNamesWithCommasWithoutManufacturingCombinations()
    {
        var source = CreateDetailedRow();
        var workOrder = source.ApprovedWorkOrder!;
        var row = source with
        {
            PlannedServiceNames = [],
            AssignedEmployeeNames = [],
            ApprovedWorkOrder = workOrder with
            {
                ServiceDetails =
                [
                    workOrder.ServiceDetails[0] with { ServiceName = "Check, inspect and release" },
                    workOrder.ServiceDetails[1]
                ],
                TaskDetails =
                [
                    workOrder.TaskDetails[0] with
                    {
                        Tools = [QuantityResource("Towbar, wide body", 2), QuantityResource("Jack", 1),
                            QuantityResource("Torque wrench", 1), QuantityResource("Wheel dolly", 1)],
                        Materials = [], GeneralSupports = []
                    }
                ]
            }
        };
        var file = FlightExportDocumentFactory.Create(FlightExportFormat.Xlsx, [row], Criteria, GeneratedAtUtc);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var breakdown = workbook.Worksheet("Flight Breakdown");
        // Two services, one parent task and four tools; no service/tool Cartesian product.
        breakdown.LastRowUsed()!.RowNumber().ShouldBe(12);
        breakdown.Range(6, 29, 12, 29).Cells().Count(cell => cell.GetString() == "Service").ShouldBe(2);
        breakdown.Range(6, 29, 12, 29).Cells().Count(cell => cell.GetString() == "Tool").ShouldBe(4);
        breakdown.Cell(6, 21).GetString().ShouldBe("Check, inspect and release");
        breakdown.Cell(9, 22).GetString().ShouldBe("Towbar, wide body");
        breakdown.Range(9, 21, 12, 21).Cells().ShouldAllBe(cell => cell.IsEmpty());
        breakdown.Range(6, 22, 7, 24).Cells().ShouldAllBe(cell => cell.IsEmpty());
    }

    [Fact]
    public void CreateWorkbook_FlightBreakdownRetainsSummaryOnlyAndNoWorkOrderFlights()
    {
        var noWorkOrder = CreateRow() with
        {
            Id = Guid.NewGuid(), ApprovedWorkOrder = null, PlannedServiceNames = [], AssignedEmployeeNames = []
        };
        var file = FlightExportDocumentFactory.Create(
            FlightExportFormat.Xlsx, [CreateRow(), noWorkOrder], Criteria, GeneratedAtUtc);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var breakdown = workbook.Worksheet("Flight Breakdown");
        // Summary-only source: planned, employee, two services, two tasks, three resources; plus bare flight.
        breakdown.LastRowUsed()!.RowNumber().ShouldBe(15);
        breakdown.Range(6, 21, 14, 21).Cells().Where(cell => !cell.IsEmpty()).Select(cell => cell.GetString())
            .ShouldBe(["Baggage", "Transit"]);
        breakdown.Range(6, 22, 14, 22).Cells().Where(cell => !cell.IsEmpty()).Select(cell => cell.GetString())
            .ShouldBe(["Towbar"]);
        breakdown.Range(6, 23, 14, 23).Cells().Where(cell => !cell.IsEmpty()).Select(cell => cell.GetString())
            .ShouldBe(["Hydraulic fluid"]);
        breakdown.Range(6, 24, 14, 24).Cells().Where(cell => !cell.IsEmpty()).Select(cell => cell.GetString())
            .ShouldBe(["GPU"]);
        breakdown.Range(6, 28, 14, 28).Cells().Where(cell => !cell.IsEmpty()).Select(cell => cell.GetString())
            .ShouldBe(["Major: Inspect landing gear", "Minor: Power aircraft systems"]);
        breakdown.Cell(15, 29).GetString().ShouldBe("Flight");
        breakdown.Cell(15, 41).GetString().ShouldBe(noWorkOrder.Id.ToString());
        breakdown.Cell(15, 42).IsEmpty().ShouldBeTrue();
        breakdown.Range(15, 20, 15, 25).Cells().ShouldAllBe(cell => cell.IsEmpty());
        breakdown.Cell(15, 28).IsEmpty().ShouldBeTrue();
        breakdown.Cell(15, 5).DataType.ShouldBe(XLDataType.DateTime);
    }

    [Fact]
    public void CreateWorkbook_EmptyFlightBreakdownHasOnlyFilterableHeaders()
    {
        var file = FlightExportDocumentFactory.Create(FlightExportFormat.Xlsx, [], Criteria, GeneratedAtUtc);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        workbook.Worksheets.Count.ShouldBe(4);
        var breakdown = workbook.Worksheet("Flight Breakdown");
        breakdown.Cell(5, 29).GetString().ShouldBe("Row Type");
        breakdown.Cell(6, 1).IsEmpty().ShouldBeTrue();
        breakdown.Cell(2, 1).GetString().ShouldStartWith("0 detail records");
        breakdown.AutoFilter.IsEnabled.ShouldBeTrue();
        breakdown.AutoFilter.Range.RangeAddress.LastAddress.RowNumber.ShouldBe(5);
        breakdown.LastRowUsed()!.RowNumber().ShouldBe(5);
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
            PlannedServiceNames = ["=Planned service"],
            AssignedEmployeeNames = ["+Assigned employee"],
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
        var breakdown = workbook.Worksheet("Flight Breakdown");
        var dangerousServiceCells = new[] { 2, 3, 5, 8, 9, 10, 11, 19, 20, 23, 24 }
            .Select(column => services.Cell(6, column));
        var dangerousTaskCells = new[] { 2, 3, 5, 8, 9, 10, 11, 19, 20, 21, 24, 25, 26, 27 }
            .Select(column => tasks.Cell(6, column));
        var dangerousBreakdownCells = new[]
        {
            breakdown.Cell(6, 20), breakdown.Cell(7, 25), breakdown.Cell(8, 21),
            breakdown.Cell(9, 28), breakdown.Cell(10, 22), breakdown.Cell(11, 23), breakdown.Cell(12, 24)
        }.Concat(Enumerable.Range(8, 5).SelectMany(rowNumber => new[] { 33, 34, 40 }
            .Select(column => breakdown.Cell(rowNumber, column))));

        dangerousServiceCells.Concat(dangerousTaskCells).Concat(dangerousBreakdownCells).ShouldAllBe(cell =>
            !cell.HasFormula && cell.Style.IncludeQuotePrefix);
        breakdown.Range(6, 1, 12, 44).Cells().ShouldAllBe(cell => !cell.HasFormula);
        services.Cell(6, 20).GetString().ShouldBe("=Service");
        services.Cell(6, 24).GetString().ShouldBe("-Service description");
        tasks.Cell(6, 25).GetString().ShouldBe("=Tool × 2");
        tasks.Cell(6, 26).GetString().ShouldBe("+Material × 1");
        tasks.Cell(6, 27).GetString().ShouldBe("@Support × 1");
        breakdown.Cell(6, 20).GetString().ShouldBe("=Planned service");
        breakdown.Cell(7, 25).GetString().ShouldBe("+Assigned employee");
        breakdown.Cell(8, 21).GetString().ShouldBe("=Service");
        breakdown.Cell(10, 22).GetString().ShouldBe("=Tool");
        breakdown.Cell(11, 23).GetString().ShouldBe("+Material");
        breakdown.Cell(12, 24).GetString().ShouldBe("@Support");
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
                .ShouldBe(["Flights", "Service Details", "Task Details", "Flight Breakdown"]);
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

    private static FlightExportRowDto CreateResourceDetailedRow()
    {
        var source = CreateDetailedRow();
        var workOrder = source.ApprovedWorkOrder!;
        return source with
        {
            ApprovedWorkOrder = workOrder with
            {
                TaskDetails =
                [
                    workOrder.TaskDetails[0] with
                    {
                        Tools =
                        [
                            QuantityResource("Towbar", 2),
                            QuantityResource("Jack", 4),
                            QuantityResource("Torque wrench", 1),
                            QuantityResource("Wheel dolly", 2)
                        ],
                        GeneralSupports = [QuantityResource("Passenger stairs", 1)]
                    },
                    workOrder.TaskDetails[1] with
                    {
                        Tools = [QuantityResource("Towbar", 1)],
                        Materials = [QuantityResource("Engine oil", 0.75m)],
                        GeneralSupports =
                        [
                            DurationResource("GPU",
                                new DateTimeOffset(2026, 7, 23, 10, 20, 0, TimeSpan.Zero),
                                new DateTimeOffset(2026, 7, 24, 12, 50, 0, TimeSpan.Zero)),
                            DurationResource("Air starter",
                                new DateTimeOffset(2026, 7, 23, 10, 20, 0, TimeSpan.Zero),
                                null)
                        ]
                    },
                    workOrder.TaskDetails[0] with
                    {
                        Id = Guid.NewGuid(),
                        Description = "Visual check without resources",
                        Tools = [],
                        Materials = [],
                        GeneralSupports = []
                    }
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
