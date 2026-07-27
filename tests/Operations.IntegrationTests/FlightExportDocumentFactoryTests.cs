using System.Text;
using ClosedXML.Excel;
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
        sheet.Cell(5, 28).IsEmpty().ShouldBeTrue();

        sheet.Cell(6, 3).GetString().ShouldBe("RJ-707");
        sheet.Cell(6, 14).GetString().ShouldBe("Royal Jordanian");
        sheet.Cell(6, 21).GetString().ShouldBe("Baggage, Transit");
        sheet.Cell(6, 22).GetString().ShouldBe("Towbar");
        sheet.Cell(6, 23).GetString().ShouldBe("Hydraulic fluid");
        sheet.Cell(6, 24).GetString().ShouldBe("GPU");
        sheet.Cell(6, 27).GetString().ShouldBe("In progress");
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
                GeneralSupportNames = ["=GPU"]
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
            "#,WO#,Flight#,WO Flight#,STA,STD,ATA,ATD,Arrival Delay,Departure Delay,Scheduled Duration,Actual Duration,Customer IATA Code,Customer Name,Station IATA Code,Station Name,Aircraft Manufacturer,Aircraft Model,Aircraft Tail Number,Planned Services,Services,Tools,Materials,General Support,Assigned Employees,Remarks,Status");
        csv.ShouldContain(",'@Towbar,");
        csv.ShouldContain(",'+Hydraulic fluid,");
        csv.ShouldContain(",'=GPU,");
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
                GeneralSupportNames = ["=GPU"]
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
            "Completed without defects"));
}
