using System.Globalization;
using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using PdfSharp.Fonts;

namespace Operations.Api.Exports;

internal enum FlightExportFormat
{
    Xlsx,
    Csv,
    Pdf
}

internal sealed record FlightExportCriteria(
    string? Search,
    Guid? StationId,
    Guid? CustomerId,
    Guid? OperationTypeId,
    IReadOnlyList<FlightStatus>? Statuses,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories,
    string? Sort);

internal sealed record FlightExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Presentation-layer document generation for the Flights list. Record selection remains in the
/// application query; this factory only turns the already-authorized projection into native files.
/// </summary>
internal static class FlightExportDocumentFactory
{
    private const string ReportTitle = "Daily Operation Report";
    private const string CompanyName = "National Aviation Ground Support";
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const string PdfContentType = "application/pdf";
    private const string FontFamily = "Open Sans";

    private const string BrandColor = "#7A3038";
    private const string BrandDarkColor = "#562128";
    private const string HeaderTextColor = "#FFFFFF";
    private const string TextColor = "#1F2937";
    private const string MutedTextColor = "#64748B";
    private const string BorderColor = "#D7DEE8";
    private const string AlternateRowColor = "#F6F8FB";
    private const string PerLandingRowColor = "#FFF3BF";
    private const string CanceledRowColor = "#FADADD";

    private static readonly object FontResolverLock = new();

    private static readonly string[] CsvHeaders =
    [
        "#", "WO#", "Flight#", "WO Flight#", "STA", "STD", "ATA", "ATD",
        "Arrival Delay", "Departure Delay", "Scheduled Duration", "Actual Duration",
        "Customer IATA Code", "Customer Name", "Station IATA Code", "Station Name",
        "Aircraft Manufacturer", "Aircraft Model", "Aircraft Tail Number", "Planned Services",
        "Services", "Assigned Employees", "Remarks", "Status"
    ];

    public static bool TryParseFormat(string? value, out FlightExportFormat format)
    {
        if (string.Equals(value, "xlsx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "excel", StringComparison.OrdinalIgnoreCase))
        {
            format = FlightExportFormat.Xlsx;
            return true;
        }

        if (string.Equals(value, "csv", StringComparison.OrdinalIgnoreCase))
        {
            format = FlightExportFormat.Csv;
            return true;
        }

        if (string.Equals(value, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            format = FlightExportFormat.Pdf;
            return true;
        }

        format = default;
        return false;
    }

    public static FlightExportFile Create(
        FlightExportFormat format,
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        var stamp = generatedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture);

        return format switch
        {
            FlightExportFormat.Xlsx => new FlightExportFile(
                CreateWorkbook(rows, criteria, generatedAtUtc),
                WorkbookContentType,
                $"flights-report-{stamp}.xlsx"),
            FlightExportFormat.Csv => new FlightExportFile(
                CreateCsv(rows),
                CsvContentType,
                $"flights-report-{stamp}.csv"),
            FlightExportFormat.Pdf => new FlightExportFile(
                CreatePdf(rows, criteria, generatedAtUtc),
                PdfContentType,
                $"flights-report-{stamp}.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported flight export format.")
        };
    }

    private static byte[] CreateWorkbook(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        const int columnCount = 24;
        const int headerRowNumber = 5;

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = ReportTitle;
        workbook.Properties.Subject = "Filtered flight operations data";
        workbook.Properties.Author = "Operations System";
        workbook.Properties.Company = "Operations System";
        workbook.Properties.Created = generatedAtUtc.UtcDateTime;

        var sheet = workbook.Worksheets.Add("Flights");
        sheet.ShowGridLines = false;

        var titleRange = sheet.Range(1, 1, 1, columnCount).Merge();
        titleRange.Value = ReportTitle;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 18;
        titleRange.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandColor);
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Row(1).Height = 34;

        var summaryRange = sheet.Range(2, 1, 2, columnCount).Merge();
        summaryRange.Value = $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} records  |  Generated {FormatReportTimestamp(generatedAtUtc)}";
        summaryRange.Style.Font.FontSize = 10;
        summaryRange.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3E9EA");
        summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 22;

        var filterRange = sheet.Range(3, 1, 3, columnCount).Merge();
        filterRange.Value = $"Scope: {BuildFilterSummary(rows, criteria)}";
        filterRange.Style.Font.FontSize = 9;
        filterRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        filterRange.Style.Alignment.WrapText = true;
        filterRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(3).Height = 30;

        for (var index = 0; index < CsvHeaders.Length; index++)
            sheet.Cell(headerRowNumber, index + 1).Value = CsvHeaders[index];

        var header = sheet.Range(headerRowNumber, 1, headerRowNumber, columnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        header.Style.Font.FontSize = 9;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandDarkColor);
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorderColor = XLColor.FromHtml(BrandDarkColor);
        sheet.Row(headerRowNumber).Height = 30;

        var rowNumber = headerRowNumber + 1;
        var sequence = 1;
        foreach (var row in rows)
        {
            WriteWorkbookRow(sheet, rowNumber, sequence++, row);

            if ((rowNumber - headerRowNumber) % 2 == 0)
                sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);

            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorderColor = XLColor.FromHtml(BorderColor);
            ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 24), row.Status);
            sheet.Row(rowNumber).Height = 20;
            rowNumber++;
        }

        var finalRow = Math.Max(headerRowNumber, rowNumber - 1);
        sheet.Range(headerRowNumber, 1, finalRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRowNumber);

        SetWorkbookColumnWidths(sheet);
        foreach (var column in new[] { 5, 6, 7, 8 })
            sheet.Column(column).Style.DateFormat.Format = "yyyy-mm-dd hh:mm \"UTC\"";
        foreach (var column in new[] { 9, 10 })
            sheet.Column(column).Style.NumberFormat.Format = "0 \"min\";-0 \"min\"";
        foreach (var column in new[] { 11, 12 })
            sheet.Column(column).Style.NumberFormat.Format = "[h]\"h \"mm\"m\"";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteWorkbookRow(IXLWorksheet sheet, int rowNumber, int sequence, FlightExportRowDto row)
    {
        var approved = row.ApprovedWorkOrder;
        sheet.Cell(rowNumber, 1).SetValue(sequence);
        sheet.Cell(rowNumber, 2).SetValue(SpreadsheetSafeText(approved?.ApprovalNumber ?? "-"));
        sheet.Cell(rowNumber, 3).SetValue(SpreadsheetSafeText(DisplayFlightNumber(row, row.FlightNumber)));
        SetOptionalText(sheet.Cell(rowNumber, 4), approved is null ? null : DisplayFlightNumber(row, approved.ActualFlightNumber));
        sheet.Cell(rowNumber, 5).SetValue(row.ScheduledArrivalUtc.UtcDateTime);
        sheet.Cell(rowNumber, 6).SetValue(row.ScheduledDepartureUtc.UtcDateTime);
        SetOptionalDate(sheet.Cell(rowNumber, 7), approved?.ActualArrivalUtc);
        SetOptionalDate(sheet.Cell(rowNumber, 8), approved?.ActualDepartureUtc);
        SetOptionalMinutes(sheet.Cell(rowNumber, 9), ArrivalDelay(row));
        SetOptionalMinutes(sheet.Cell(rowNumber, 10), DepartureDelay(row));
        SetOptionalDuration(sheet.Cell(rowNumber, 11), ScheduledDuration(row));
        SetOptionalDuration(sheet.Cell(rowNumber, 12), ActualDuration(row));
        SetOptionalText(sheet.Cell(rowNumber, 13), row.CustomerIataCode);
        sheet.Cell(rowNumber, 14).SetValue(SpreadsheetSafeText(row.CustomerName));
        sheet.Cell(rowNumber, 15).SetValue(SpreadsheetSafeText(row.StationIata));
        sheet.Cell(rowNumber, 16).SetValue(SpreadsheetSafeText(row.StationName));
        SetOptionalText(sheet.Cell(rowNumber, 17), approved?.AircraftManufacturer);
        SetOptionalText(sheet.Cell(rowNumber, 18), approved?.AircraftModel);
        SetOptionalText(sheet.Cell(rowNumber, 19), approved?.AircraftTailNumber);
        SetOptionalText(sheet.Cell(rowNumber, 20), JoinNames(row.PlannedServiceNames));
        SetOptionalText(sheet.Cell(rowNumber, 21), approved is null ? null : JoinNames(approved.ServiceNames));
        SetOptionalText(sheet.Cell(rowNumber, 22), JoinNames(row.AssignedEmployeeNames));
        SetOptionalText(sheet.Cell(rowNumber, 23), approved?.Remarks);
        sheet.Cell(rowNumber, 24).SetValue(StatusLabel(row.Status));

        var rowRange = sheet.Range(rowNumber, 1, rowNumber, 24);
        rowRange.Style.Font.FontSize = 9;
        rowRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void ApplyWorkbookStatusStyle(IXLCell cell, string status)
    {
        var (background, foreground) = status switch
        {
            "Completed" => ("#DDF7E7", "#18733A"),
            "Canceled" or "Merged" => ("#FDE5E7", "#A32632"),
            "InProgress" => ("#FFF2C7", "#9A5800"),
            _ => ("#E9EEF5", "#536274")
        };

        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(background);
        cell.Style.Font.FontColor = XLColor.FromHtml(foreground);
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void SetWorkbookColumnWidths(IXLWorksheet sheet)
    {
        var widths = new[] { 7d, 16, 18, 18, 21, 21, 21, 21, 16, 18, 19, 17, 18, 28, 18, 26, 22, 20, 20, 35, 35, 35, 40, 16 };
        for (var index = 0; index < widths.Length; index++)
            sheet.Column(index + 1).Width = widths[index];
    }

    private static byte[] CreateCsv(IReadOnlyList<FlightExportRowDto> rows)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetPreamble());

        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true))
        {
            WriteCsvRow(writer, CsvHeaders);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var approved = row.ApprovedWorkOrder;
                WriteCsvRow(writer,
                [
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    SpreadsheetSafeText(approved?.ApprovalNumber ?? "-"),
                    SpreadsheetSafeText(DisplayFlightNumber(row, row.FlightNumber)),
                    SpreadsheetSafeText(approved is null ? string.Empty : DisplayFlightNumber(row, approved.ActualFlightNumber)),
                    FormatCsvTimestamp(row.ScheduledArrivalUtc), FormatCsvTimestamp(row.ScheduledDepartureUtc),
                    FormatCsvTimestamp(approved?.ActualArrivalUtc), FormatCsvTimestamp(approved?.ActualDepartureUtc),
                    FormatCsvDuration(ArrivalDelay(row)), FormatCsvDuration(DepartureDelay(row)),
                    FormatCsvDuration(ScheduledDuration(row)), FormatCsvDuration(ActualDuration(row)),
                    SpreadsheetSafeText(row.CustomerIataCode ?? string.Empty), SpreadsheetSafeText(row.CustomerName),
                    SpreadsheetSafeText(row.StationIata), SpreadsheetSafeText(row.StationName),
                    SpreadsheetSafeText(approved?.AircraftManufacturer ?? string.Empty),
                    SpreadsheetSafeText(approved?.AircraftModel ?? string.Empty),
                    SpreadsheetSafeText(approved?.AircraftTailNumber ?? string.Empty),
                    SpreadsheetSafeText(JoinNames(row.PlannedServiceNames)),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.ServiceNames)),
                    SpreadsheetSafeText(JoinNames(row.AssignedEmployeeNames)),
                    SpreadsheetSafeText(approved?.Remarks ?? string.Empty), StatusLabel(row.Status)
                ]);
            }
        }

        return stream.ToArray();
    }

    private static void WriteCsvRow(TextWriter writer, IReadOnlyList<string> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
                writer.Write(',');

            var field = fields[index] ?? string.Empty;
            var needsQuotes = field.Contains(',') || field.Contains('"') || field.Contains('\r') || field.Contains('\n');
            if (needsQuotes)
            {
                writer.Write('"');
                writer.Write(field.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else
            {
                writer.Write(field);
            }
        }

        writer.Write("\r\n");
    }

    private static byte[] CreatePdf(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        EnsurePdfFontResolver();

        var document = new Document();
        document.Info.Title = ReportTitle;
        document.Info.Subject = "Filtered flight operations data";
        document.Info.Author = "Operations System";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontFamily;
        normal.Font.Size = Unit.FromPoint(8);
        normal.Font.Color = Color.Parse(TextColor);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.05);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.35);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.15);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.15);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(0.45);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.45);

        AddPdfFooter(section, generatedAtUtc);
        AddPdfFirstPageHeader(section, rows, criteria);

        if (rows.Count == 0)
        {
            var empty = section.AddParagraph("No flights match the selected filters.");
            empty.Format.Font.Size = Unit.FromPoint(11);
            empty.Format.Font.Color = Color.Parse(MutedTextColor);
            empty.Format.SpaceBefore = Unit.FromCentimeter(1.2);
            empty.Format.Alignment = ParagraphAlignment.Center;
        }
        else
        {
            AddPdfTable(section, rows, criteria);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void AddPdfFirstPageHeader(
        Section section,
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria)
    {
        var masthead = section.AddTable();
        masthead.Borders.Bottom.Color = Color.Parse(BrandColor);
        masthead.Borders.Bottom.Width = Unit.FromPoint(1.2);
        AddPdfColumn(masthead, 4.0, ParagraphAlignment.Left);
        AddPdfColumn(masthead, 17.4, ParagraphAlignment.Center);
        AddPdfColumn(masthead, 4.0, ParagraphAlignment.Right);
        var mastheadRow = masthead.AddRow();
        mastheadRow.VerticalAlignment = VerticalAlignment.Center;
        mastheadRow.BottomPadding = Unit.FromPoint(7);

        var logo = mastheadRow.Cells[0].AddImage(GetLogoDataUri());
        logo.LockAspectRatio = true;
        logo.Height = Unit.FromCentimeter(1.5);

        var company = mastheadRow.Cells[1].AddParagraph();
        company.Format.Alignment = ParagraphAlignment.Center;
        company.Format.Font.Size = Unit.FromPoint(15);
        company.Format.Font.Bold = true;
        company.Format.Font.Color = Color.Parse(BrandDarkColor);
        company.AddText(CompanyName);
        var report = mastheadRow.Cells[1].AddParagraph(ReportTitle);
        report.Format.Alignment = ParagraphAlignment.Center;
        report.Format.Font.Size = Unit.FromPoint(11);
        report.Format.Font.Color = Color.Parse(BrandColor);
        report.Format.SpaceBefore = Unit.FromPoint(2);

        var date = mastheadRow.Cells[2].AddParagraph(PdfSafeText(PdfDateScope(criteria)));
        date.Format.Alignment = ParagraphAlignment.Right;
        date.Format.Font.Size = Unit.FromPoint(8);
        date.Format.Font.Bold = true;
        date.Format.Font.Color = Color.Parse(TextColor);

        var scopeLines = BuildPdfScopeLines(rows, criteria);
        if (scopeLines.Count > 0)
        {
            var scopeTable = section.AddTable();
            scopeTable.Rows.LeftIndent = Unit.Zero;
            AddPdfColumn(scopeTable, 25.4, ParagraphAlignment.Left);
            var scopeRow = scopeTable.AddRow();
            scopeRow.TopPadding = Unit.FromPoint(4);
            scopeRow.BottomPadding = Unit.FromPoint(4);
            var scopeCell = scopeRow.Cells[0];
            scopeCell.Shading.Color = Color.Parse("#F7F1F2");
            scopeCell.Borders.Color = Color.Parse("#E4CBCD");
            scopeCell.Borders.Width = Unit.FromPoint(0.5);
            var scope = scopeCell.AddParagraph();
            scope.Format.Font.Size = Unit.FromPoint(8);
            scope.Format.Font.Color = Color.Parse(TextColor);
            for (var index = 0; index < scopeLines.Count; index++)
            {
                if (index > 0)
                    scope.AddLineBreak();
                scope.AddFormattedText(PdfSafeText(scopeLines[index]), TextFormat.Bold);
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void AddPdfFooter(Section section, DateTimeOffset generatedAtUtc)
    {
        var legend = section.Footers.Primary.AddTable();
        legend.Rows.LeftIndent = Unit.Zero;
        AddPdfColumn(legend, 0.35, ParagraphAlignment.Center);
        AddPdfColumn(legend, 2.0, ParagraphAlignment.Left);
        AddPdfColumn(legend, 0.35, ParagraphAlignment.Center);
        AddPdfColumn(legend, 2.0, ParagraphAlignment.Left);
        var legendRow = legend.AddRow();
        legendRow.Cells[0].Shading.Color = Color.Parse(PerLandingRowColor);
        legendRow.Cells[1].AddParagraph("Per Landing");
        legendRow.Cells[2].Shading.Color = Color.Parse(CanceledRowColor);
        legendRow.Cells[3].AddParagraph("Canceled");
        legend.Format.Font.Name = FontFamily;
        legend.Format.Font.Size = Unit.FromPoint(5.5);
        legendRow.TopPadding = Unit.FromPoint(1);
        legendRow.BottomPadding = Unit.FromPoint(2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = FontFamily;
        footer.Format.Font.Size = Unit.FromPoint(7);
        footer.Format.Font.Color = Color.Parse(MutedTextColor);
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Color = Color.Parse(BorderColor);
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.SpaceBefore = Unit.FromPoint(4);
        footer.AddText($"Generated {FormatReportTimestamp(generatedAtUtc)}   |   Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void AddPdfTable(Section section, IReadOnlyList<FlightExportRowDto> rows, FlightExportCriteria criteria)
    {
        var columns = BuildPdfColumns(criteria);
        var table = section.AddTable();
        table.Rows.LeftIndent = Unit.Zero;
        table.Borders.Color = Color.Parse(BorderColor);
        table.Borders.Width = Unit.FromPoint(0.35);
        table.Format.Font.Name = FontFamily;
        table.Format.Font.Size = Unit.FromPoint(7.2);

        foreach (var column in columns)
            AddPdfColumn(table, column.WidthCentimeters, column.Alignment);

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = Color.Parse(HeaderTextColor);
        heading.Format.Font.Size = Unit.FromPoint(7.1);
        heading.Shading.Color = Color.Parse(BrandDarkColor);
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.TopPadding = Unit.FromPoint(5);
        heading.BottomPadding = Unit.FromPoint(5);

        for (var index = 0; index < columns.Count; index++)
            heading.Cells[index].AddParagraph(columns[index].Header);

        for (var index = 0; index < rows.Count; index++)
        {
            var flight = rows[index];
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.TopPadding = Unit.FromPoint(3.5);
            row.BottomPadding = Unit.FromPoint(3.5);

            if (flight.Status is "Canceled" or "Merged")
                row.Shading.Color = Color.Parse(CanceledRowColor);
            else if (flight.IsPerLanding)
                row.Shading.Color = Color.Parse(PerLandingRowColor);
            else if (index % 2 == 1)
                row.Shading.Color = Color.Parse(AlternateRowColor);

            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                AddPdfCell(row.Cells[columnIndex], columns[columnIndex].Value(flight, index + 1), bold: columnIndex <= 2);
        }
    }

    private static IReadOnlyList<PdfColumnSpec> BuildPdfColumns(FlightExportCriteria criteria)
    {
        var columns = new List<PdfColumnSpec>
        {
            new("#", 0.8, ParagraphAlignment.Center, (_, sequence) => sequence.ToString(CultureInfo.InvariantCulture)),
            new("WO#", 2.2, ParagraphAlignment.Center, (row, _) => row.ApprovedWorkOrder?.ApprovalNumber ?? "-"),
            new("Flight#", 2.1, ParagraphAlignment.Left, (row, _) => DisplayFlightNumber(row, row.ApprovedWorkOrder?.ActualFlightNumber ?? row.FlightNumber))
        };
        if (!criteria.CustomerId.HasValue)
            columns.Add(new("Customer", 5.0, ParagraphAlignment.Left, (row, _) => row.CustomerName));
        if (!criteria.StationId.HasValue)
            columns.Add(new("Station", 2.5, ParagraphAlignment.Left, (row, _) => row.StationName));

        var reclaimed = (criteria.CustomerId.HasValue ? 2.5 : 0) + (criteria.StationId.HasValue ? 1.25 : 0);
        columns.Add(new("Aircraft", 2.4, ParagraphAlignment.Left, (row, _) => row.ApprovedWorkOrder?.AircraftModel ?? string.Empty));
        columns.Add(new("Services", 5.0 + reclaimed, ParagraphAlignment.Left, (row, _) => PdfServices(row)));
        columns.Add(new("Remarks", 5.4 + reclaimed, ParagraphAlignment.Left, (row, _) => row.ApprovedWorkOrder?.Remarks ?? string.Empty));
        return columns;
    }

    private sealed record PdfColumnSpec(
        string Header,
        double WidthCentimeters,
        ParagraphAlignment Alignment,
        Func<FlightExportRowDto, int, string> Value);

    private static void AddPdfColumn(Table table, double centimeters, ParagraphAlignment alignment)
    {
        var column = table.AddColumn(Unit.FromCentimeter(centimeters));
        column.Format.Alignment = alignment;
    }

    private static void AddPdfCell(Cell cell, string value, bool bold = false, Color? color = null)
    {
        var paragraph = cell.AddParagraph(PdfSafeText(value));
        paragraph.Format.Font.Bold = bold;
        if (color is { } fontColor)
            paragraph.Format.Font.Color = fontColor;
    }

    private static Color PdfStatusColor(string status) => status switch
    {
        "Completed" => Color.Parse("#18733A"),
        "Canceled" or "Merged" => Color.Parse("#A32632"),
        "InProgress" => Color.Parse("#9A5800"),
        _ => Color.Parse("#536274")
    };

    private static string BuildFilterSummary(IReadOnlyList<FlightExportRowDto> rows, FlightExportCriteria criteria)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
            filters.Add($"Search: {criteria.Search.Trim()}");

        if (criteria.StationId.HasValue)
        {
            var station = rows.FirstOrDefault();
            filters.Add(station is null ? "Station filter applied" : $"Station: {station.StationIata} - {station.StationName}");
        }

        if (criteria.CustomerId.HasValue)
        {
            var customer = rows.FirstOrDefault();
            filters.Add(customer is null ? "Customer filter applied" : $"Customer: {CustomerDisplay(customer)}");
        }

        if (criteria.FromUtc is { } from && criteria.ToUtc is { } to)
            filters.Add($"Scheduled arrival: {from.UtcDateTime:yyyy-MM-dd} to {to.UtcDateTime:yyyy-MM-dd}");
        else if (criteria.FromUtc is { } fromOnly)
            filters.Add($"Scheduled arrival from: {fromOnly.UtcDateTime:yyyy-MM-dd}");
        else if (criteria.ToUtc is { } toOnly)
            filters.Add($"Scheduled arrival through: {toOnly.UtcDateTime:yyyy-MM-dd}");

        if (criteria.ServiceCategories is { Count: > 0 } categories)
            filters.Add($"Service category: {string.Join(", ", categories.Select(ServiceCategoryLabel))}");

        return filters.Count == 0
            ? "All flights within your authorized scope"
            : string.Join("  |  ", filters);
    }

    private static string ServiceCategoryLabel(FlightServiceCategory category) => category switch
    {
        FlightServiceCategory.PerLanding => "Per Landing",
        FlightServiceCategory.OnCall => "On Call",
        _ => "Other"
    };

    private static string SpreadsheetSafeText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var candidate = value.AsSpan().TrimStart();
        if (candidate.IsEmpty)
            return value;

        return candidate[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? $"'{value}"
            : value;
    }

    private static string PdfSafeText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsControl(character) || character is '\t' or '\n')
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static string DisplayFlightNumber(FlightExportRowDto row, string flightNumber) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? flightNumber
            : $"{row.CustomerIataCode.Trim().ToUpperInvariant()}-{flightNumber}";

    private static string CustomerDisplay(FlightExportRowDto row) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? row.CustomerName
            : $"{row.CustomerIataCode.Trim().ToUpperInvariant()} - {row.CustomerName}";

    private static TimeSpan? ScheduledDuration(FlightExportRowDto row) =>
        NonNegative(row.ScheduledDepartureUtc - row.ScheduledArrivalUtc);

    private static TimeSpan? ActualDuration(FlightExportRowDto row) =>
        row.ApprovedWorkOrder is { ActualArrivalUtc: { } ata, ActualDepartureUtc: { } atd }
            ? NonNegative(atd - ata)
            : null;

    private static TimeSpan? ArrivalDelay(FlightExportRowDto row) =>
        row.ApprovedWorkOrder?.ActualArrivalUtc is { } ata ? ata - row.ScheduledArrivalUtc : null;

    private static TimeSpan? DepartureDelay(FlightExportRowDto row) =>
        row.ApprovedWorkOrder?.ActualDepartureUtc is { } atd ? atd - row.ScheduledDepartureUtc : null;

    private static TimeSpan? NonNegative(TimeSpan value) => value < TimeSpan.Zero ? null : value;

    private static void SetOptionalDate(IXLCell cell, DateTimeOffset? value)
    {
        if (value.HasValue)
            cell.SetValue(value.Value.UtcDateTime);
    }

    private static void SetOptionalText(IXLCell cell, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            cell.SetValue(SpreadsheetSafeText(value));
    }

    private static void SetOptionalDuration(IXLCell cell, TimeSpan? value)
    {
        if (value.HasValue)
            cell.SetValue(value.Value);
    }

    private static void SetOptionalMinutes(IXLCell cell, TimeSpan? value)
    {
        if (value.HasValue)
            cell.SetValue(Math.Round(value.Value.TotalMinutes));
    }

    private static string JoinNames(IReadOnlyList<string> names) => string.Join(", ", names);

    private static string PdfServices(FlightExportRowDto row)
    {
        if (row.ApprovedWorkOrder is null)
            return string.Empty;
        if (row.ApprovedWorkOrder.ServiceNames.Count == 0 && row.IsPerLanding)
            return "Per Landing";
        return JoinNames(row.ApprovedWorkOrder.ServiceNames);
    }

    private static string FormatCsvTimestamp(DateTimeOffset? value) => value?.UtcDateTime
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatCsvDuration(TimeSpan? value) => value.HasValue
        ? Math.Round(value.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    private static string PdfDateScope(FlightExportCriteria criteria)
    {
        if (criteria.FromUtc is { } from && criteria.ToUtc is { } to)
            return $"From {from.UtcDateTime:dd MMM yyyy HH:mm} UTC\nTo   {to.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        if (criteria.FromUtc is { } fromOnly)
            return $"From {fromOnly.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        if (criteria.ToUtc is { } toOnly)
            return $"To   {toOnly.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        return string.Empty;
    }

    private static IReadOnlyList<string> BuildPdfScopeLines(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria)
    {
        var lines = new List<string>();
        if (criteria.CustomerId.HasValue)
            lines.Add(rows.FirstOrDefault() is { } row ? $"Customer: {CustomerDisplay(row)}" : "Customer filter applied");
        if (criteria.StationId.HasValue)
            lines.Add(rows.FirstOrDefault() is { } row ? $"Station: {row.StationIata} - {row.StationName}" : "Station filter applied");
        if (!string.IsNullOrWhiteSpace(criteria.Search))
            lines.Add($"Search: {criteria.Search.Trim()}");
        return lines;
    }

    private static string GetLogoDataUri()
    {
        using var stream = typeof(FlightExportDocumentFactory).Assembly
            .GetManifestResourceStream("Operations.Api.Assets.NagsLogo.png")
            ?? throw new InvalidOperationException("The report logo resource is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return "base64:" + Convert.ToBase64String(memory.ToArray());
    }

    private static string StatusLabel(string status) => status switch
    {
        "InProgress" => "In progress",
        _ => status
    };

    private static string FormatReportTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatPdfTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static void EnsurePdfFontResolver()
    {
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new OpenSansFontResolver();
        }
    }

    private sealed class OpenSansFontResolver : IFontResolver
    {
        private const string RegularFace = "OperationsOpenSansRegular";
        private const string BoldFace = "OperationsOpenSansBold";

        private static readonly Lazy<byte[]> RegularFont = new(() => LoadFont("Operations.Api.Fonts.OpenSans-Regular.ttf"));
        private static readonly Lazy<byte[]> BoldFont = new(() => LoadFont("Operations.Api.Fonts.OpenSans-Bold.ttf"));

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace, mustSimulateBold: false, mustSimulateItalic: isItalic);

        public byte[]? GetFont(string faceName) => faceName switch
        {
            RegularFace => RegularFont.Value,
            BoldFace => BoldFont.Value,
            _ => null
        };

        private static byte[] LoadFont(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded PDF font '{resourceName}' was not found.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
