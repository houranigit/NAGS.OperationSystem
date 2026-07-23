using System.Text;
using ClosedXML.Excel;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class DashboardFlightExportDocumentFactoryTests
{
    private static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 7, 23, 19, 45, 0, TimeSpan.Zero);

    private static readonly DashboardFlightExportCriteria Criteria = new(
        new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero),
        [],
        [],
        []);

    [Theory]
    [InlineData("xlsx", "Xlsx")]
    [InlineData("excel", "Xlsx")]
    [InlineData("csv", "Csv")]
    [InlineData("pdf", "Pdf")]
    public void TryParseFormat_AcceptsSupportedTypes(string value, string expected)
    {
        DashboardFlightExportDocumentFactory.TryParseFormat(value, out var actual).ShouldBeTrue();
        actual.ToString().ShouldBe(expected);
    }

    [Fact]
    public void CreateWorkbook_UsesDashboardColumnsAndKeepsStaInsideFlightCell()
    {
        var file = DashboardFlightExportDocumentFactory.Create(
            DashboardFlightExportFormat.Xlsx,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.FileName.ShouldEndWith(".xlsx");

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Dashboard Flights");

        sheet.Cell(5, 1).GetString().ShouldBe("#");
        sheet.Cell(5, 2).GetString().ShouldBe("Flight / STA (UTC)");
        sheet.Cell(5, 3).GetString().ShouldBe("Customer code");
        sheet.Cell(5, 4).GetString().ShouldBe("Station code");
        sheet.Cell(5, 5).GetString().ShouldBe("Operation type");
        sheet.Cell(5, 6).GetString().ShouldBe("Performed services");
        sheet.Cell(5, 7).GetString().ShouldBe("Status");
        sheet.Cell(5, 8).IsEmpty().ShouldBeTrue();

        sheet.Cell(6, 2).GetString().ShouldContain("STA 23 Jul 2026 09:30 UTC");
        sheet.Cell(6, 3).GetString().ShouldBe("RJ");
        sheet.Cell(6, 3).GetString().ShouldNotContain("Royal Jordanian");
        sheet.Cell(6, 4).GetString().ShouldBe("AMM");
        sheet.Cell(6, 4).GetString().ShouldNotContain("Queen Alia");
        sheet.Cell(6, 5).GetString().ShouldBe("Arrival");
        sheet.Cell(6, 7).GetString().ShouldBe("In progress");
        sheet.Cell(6, 2).HasFormula.ShouldBeFalse();
        sheet.Cell(6, 6).HasFormula.ShouldBeFalse();

        Enumerable.Range(1, 7)
            .Select(column => sheet.Cell(5, column).GetString())
            .ShouldNotContain("STD (UTC)");
    }

    [Fact]
    public void CreateCsv_UsesUtf8BomAndProtectsSpreadsheetFormulaValues()
    {
        var row = CreateRow() with
        {
            CustomerIataCode = "=RJ",
            FlightNumber = "1+1",
            OperationTypeName = "+Arrival",
            PerformedServiceNames = ["@Transit"]
        };

        var file = DashboardFlightExportDocumentFactory.Create(
            DashboardFlightExportFormat.Csv,
            [row],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("text/csv; charset=utf-8");
        file.Content.Take(3).ShouldBe([(byte)0xEF, (byte)0xBB, (byte)0xBF]);

        var csv = Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3);
        csv.Split('\n', 2)[0].TrimEnd('\r')
            .ShouldBe("#,Flight / STA (UTC),Customer code,Station code,Operation type,Performed services,Status");
        csv.ShouldContain("'=RJ-1+1 | STA 23 Jul 2026 09:30 UTC");
        csv.ShouldContain(",'=RJ,");
        csv.ShouldContain(",'+Arrival,");
        csv.ShouldContain(",'@Transit,");
        csv.ShouldNotContain("Queen Alia");
        csv.ShouldNotContain("Scheduled departure");
    }

    [Fact]
    public void CreatePdf_ReturnsNativePdf()
    {
        var file = DashboardFlightExportDocumentFactory.Create(
            DashboardFlightExportFormat.Pdf,
            [CreateRow()],
            Criteria,
            GeneratedAtUtc);

        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldEndWith(".pdf");
        Encoding.ASCII.GetString(file.Content, 0, 5).ShouldBe("%PDF-");
    }

    private static DashboardFlightRowDto CreateRow() => new(
        Guid.Parse("7a4d568c-85aa-4175-a32f-fec41dc05d61"),
        "707",
        "RJ",
        "Royal Jordanian",
        Guid.Parse("89c56ce1-952c-4b89-99af-e4a54cb50938"),
        "AMM",
        "Queen Alia International Airport",
        "Arrival",
        new DateTimeOffset(2026, 7, 23, 9, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 23, 11, 0, 0, TimeSpan.Zero),
        "InProgress",
        ["Transit", "Baggage"]);
}
