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
    FlightStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Sort);

internal sealed record FlightExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Presentation-layer document generation for the Flights list. Record selection remains in the
/// application query; this factory only turns the already-authorized projection into native files.
/// </summary>
internal static class FlightExportDocumentFactory
{
    private const string ReportTitle = "Flight Operations Report";
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

    private static readonly object FontResolverLock = new();

    private static readonly string[] CsvHeaders =
    [
        "Flight ID",
        "Flight Number",
        "Original Flight Number",
        "Customer IATA",
        "Customer",
        "Station",
        "Station Name",
        "Operation",
        "Scheduled Arrival (UTC)",
        "Scheduled Departure (UTC)",
        "Duration (minutes)",
        "Status",
        "Flight Type"
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
        const int columnCount = 13;
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
        foreach (var row in rows)
        {
            WriteWorkbookRow(sheet, rowNumber, row);

            if ((rowNumber - headerRowNumber) % 2 == 0)
                sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);

            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorderColor = XLColor.FromHtml(BorderColor);
            ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 12), row.Status);
            sheet.Row(rowNumber).Height = 20;
            rowNumber++;
        }

        var finalRow = Math.Max(headerRowNumber, rowNumber - 1);
        sheet.Range(headerRowNumber, 1, finalRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRowNumber);

        SetWorkbookColumnWidths(sheet);
        sheet.Column(9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm \"UTC\"";
        sheet.Column(10).Style.DateFormat.Format = "yyyy-mm-dd hh:mm \"UTC\"";
        sheet.Column(11).Style.NumberFormat.Format = "[h]\"h \"mm\"m\"";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteWorkbookRow(IXLWorksheet sheet, int rowNumber, FlightExportRowDto row)
    {
        sheet.Cell(rowNumber, 1).SetValue(row.Id.ToString("D"));
        sheet.Cell(rowNumber, 2).SetValue(SpreadsheetSafeText(DisplayFlightNumber(row)));
        sheet.Cell(rowNumber, 3).SetValue(SpreadsheetSafeText(row.OriginalFlightNumber));
        sheet.Cell(rowNumber, 4).SetValue(SpreadsheetSafeText(row.CustomerIataCode ?? string.Empty));
        sheet.Cell(rowNumber, 5).SetValue(SpreadsheetSafeText(row.CustomerName));
        sheet.Cell(rowNumber, 6).SetValue(SpreadsheetSafeText(row.StationIata));
        sheet.Cell(rowNumber, 7).SetValue(SpreadsheetSafeText(row.StationName));
        sheet.Cell(rowNumber, 8).SetValue(SpreadsheetSafeText(row.OperationTypeName));
        sheet.Cell(rowNumber, 9).SetValue(row.ScheduledArrivalUtc.UtcDateTime);
        sheet.Cell(rowNumber, 10).SetValue(row.ScheduledDepartureUtc.UtcDateTime);

        var duration = FlightDuration(row);
        if (duration is { } validDuration)
            sheet.Cell(rowNumber, 11).SetValue(validDuration);
        else
            sheet.Cell(rowNumber, 11).SetValue("-");

        var statusCell = sheet.Cell(rowNumber, 12);
        statusCell.SetValue(StatusLabel(row.Status));

        sheet.Cell(rowNumber, 13).SetValue(row.IsPerLanding ? "Per landing" : "Standard");

        var rowRange = sheet.Range(rowNumber, 1, rowNumber, 13);
        rowRange.Style.Font.FontSize = 9;
        rowRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Cell(rowNumber, 1).Style.Font.FontName = "Consolas";
        sheet.Cell(rowNumber, 1).Style.Font.FontSize = 8;
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
        sheet.Column(1).Width = 38;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 20;
        sheet.Column(4).Width = 14;
        sheet.Column(5).Width = 30;
        sheet.Column(6).Width = 12;
        sheet.Column(7).Width = 24;
        sheet.Column(8).Width = 22;
        sheet.Column(9).Width = 23;
        sheet.Column(10).Width = 23;
        sheet.Column(11).Width = 18;
        sheet.Column(12).Width = 16;
        sheet.Column(13).Width = 16;
    }

    private static byte[] CreateCsv(IReadOnlyList<FlightExportRowDto> rows)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetPreamble());

        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true))
        {
            WriteCsvRow(writer, CsvHeaders);

            foreach (var row in rows)
            {
                var duration = FlightDuration(row);
                WriteCsvRow(writer,
                [
                    row.Id.ToString("D"),
                    SpreadsheetSafeText(DisplayFlightNumber(row)),
                    SpreadsheetSafeText(row.OriginalFlightNumber),
                    SpreadsheetSafeText(row.CustomerIataCode ?? string.Empty),
                    SpreadsheetSafeText(row.CustomerName),
                    SpreadsheetSafeText(row.StationIata),
                    SpreadsheetSafeText(row.StationName),
                    SpreadsheetSafeText(row.OperationTypeName),
                    row.ScheduledArrivalUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    row.ScheduledDepartureUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    duration is null ? string.Empty : Math.Round(duration.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture),
                    StatusLabel(row.Status),
                    row.IsPerLanding ? "Per landing" : "Standard"
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
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.35);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.35);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.15);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.15);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(0.45);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.55);

        AddPdfHeader(section);
        AddPdfFooter(section, generatedAtUtc);

        var title = section.AddParagraph(ReportTitle);
        title.Format.Font.Name = FontFamily;
        title.Format.Font.Size = Unit.FromPoint(19);
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.Parse(BrandColor);
        title.Format.SpaceAfter = Unit.FromPoint(3);

        var summary = section.AddParagraph();
        summary.Format.Font.Size = Unit.FromPoint(8.5);
        summary.Format.Font.Color = Color.Parse(MutedTextColor);
        summary.AddFormattedText(
            $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} records",
            TextFormat.Bold);
        summary.AddText($"   |   Generated {FormatReportTimestamp(generatedAtUtc)}");
        summary.Format.SpaceAfter = Unit.FromPoint(4);

        var filters = section.AddParagraph();
        filters.Format.Font.Size = Unit.FromPoint(8);
        filters.Format.Font.Color = Color.Parse(TextColor);
        filters.Format.Shading.Color = Color.Parse("#F3E9EA");
        filters.Format.Borders.Color = Color.Parse("#E4CBCD");
        filters.Format.Borders.Width = Unit.FromPoint(0.5);
        filters.Format.LeftIndent = Unit.FromPoint(5);
        filters.Format.RightIndent = Unit.FromPoint(5);
        filters.Format.SpaceBefore = Unit.FromPoint(2);
        filters.Format.SpaceAfter = Unit.FromPoint(8);
        filters.AddFormattedText("Scope: ", TextFormat.Bold);
        filters.AddText(PdfSafeText(BuildFilterSummary(rows, criteria)));

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
            AddPdfTable(section, rows);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void AddPdfHeader(Section section)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.AddFormattedText("OPERATIONS SYSTEM", TextFormat.Bold);
        header.AddText("   /   FLIGHTS");
        header.Format.Font.Name = FontFamily;
        header.Format.Font.Size = Unit.FromPoint(7.5);
        header.Format.Font.Color = Color.Parse(BrandColor);
        header.Format.Borders.Bottom.Color = Color.Parse("#D9BFC2");
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.75);
        header.Format.SpaceAfter = Unit.FromPoint(3);
    }

    private static void AddPdfFooter(Section section, DateTimeOffset generatedAtUtc)
    {
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

    private static void AddPdfTable(Section section, IReadOnlyList<FlightExportRowDto> rows)
    {
        var table = section.AddTable();
        table.Rows.LeftIndent = Unit.Zero;
        table.Borders.Color = Color.Parse(BorderColor);
        table.Borders.Width = Unit.FromPoint(0.35);
        table.Format.Font.Name = FontFamily;
        table.Format.Font.Size = Unit.FromPoint(7.2);

        AddPdfColumn(table, 2.3, ParagraphAlignment.Left);
        AddPdfColumn(table, 4.6, ParagraphAlignment.Left);
        AddPdfColumn(table, 1.4, ParagraphAlignment.Center);
        AddPdfColumn(table, 2.8, ParagraphAlignment.Left);
        AddPdfColumn(table, 3.05, ParagraphAlignment.Center);
        AddPdfColumn(table, 3.05, ParagraphAlignment.Center);
        AddPdfColumn(table, 1.8, ParagraphAlignment.Center);
        AddPdfColumn(table, 2.1, ParagraphAlignment.Center);
        AddPdfColumn(table, 1.8, ParagraphAlignment.Center);

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = Color.Parse(HeaderTextColor);
        heading.Format.Font.Size = Unit.FromPoint(7.1);
        heading.Shading.Color = Color.Parse(BrandDarkColor);
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.TopPadding = Unit.FromPoint(5);
        heading.BottomPadding = Unit.FromPoint(5);

        var headers = new[] { "Flight #", "Customer", "Station", "Operation", "STA (UTC)", "STD (UTC)", "Duration", "Status", "Type" };
        for (var index = 0; index < headers.Length; index++)
            heading.Cells[index].AddParagraph(headers[index]);

        for (var index = 0; index < rows.Count; index++)
        {
            var flight = rows[index];
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.TopPadding = Unit.FromPoint(3.5);
            row.BottomPadding = Unit.FromPoint(3.5);

            if (index % 2 == 1)
                row.Shading.Color = Color.Parse(AlternateRowColor);

            AddPdfCell(row.Cells[0], DisplayFlightNumber(flight), bold: true);
            AddPdfCell(row.Cells[1], CustomerDisplay(flight));
            AddPdfCell(row.Cells[2], flight.StationIata);
            AddPdfCell(row.Cells[3], flight.OperationTypeName);
            AddPdfCell(row.Cells[4], FormatPdfTimestamp(flight.ScheduledArrivalUtc));
            AddPdfCell(row.Cells[5], FormatPdfTimestamp(flight.ScheduledDepartureUtc));
            AddPdfCell(row.Cells[6], DurationDisplay(flight));
            AddPdfCell(row.Cells[7], StatusLabel(flight.Status), bold: true, color: PdfStatusColor(flight.Status));
            AddPdfCell(row.Cells[8], flight.IsPerLanding ? "Per landing" : "Standard");
        }
    }

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

        if (criteria.OperationTypeId.HasValue)
        {
            var operation = rows.FirstOrDefault();
            filters.Add(operation is null ? "Operation filter applied" : $"Operation: {operation.OperationTypeName}");
        }

        if (criteria.Status is { } status)
            filters.Add($"Status: {StatusLabel(status.ToString())}");

        if (criteria.FromUtc is { } from && criteria.ToUtc is { } to)
            filters.Add($"Scheduled arrival: {from.UtcDateTime:yyyy-MM-dd} to {to.UtcDateTime:yyyy-MM-dd}");
        else if (criteria.FromUtc is { } fromOnly)
            filters.Add($"Scheduled arrival from: {fromOnly.UtcDateTime:yyyy-MM-dd}");
        else if (criteria.ToUtc is { } toOnly)
            filters.Add($"Scheduled arrival through: {toOnly.UtcDateTime:yyyy-MM-dd}");

        return filters.Count == 0
            ? "All flights within your authorized scope"
            : string.Join("  |  ", filters);
    }

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

    private static string DisplayFlightNumber(FlightExportRowDto row) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? row.FlightNumber
            : $"{row.CustomerIataCode.Trim().ToUpperInvariant()}-{row.FlightNumber}";

    private static string CustomerDisplay(FlightExportRowDto row) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? row.CustomerName
            : $"{row.CustomerIataCode.Trim().ToUpperInvariant()} - {row.CustomerName}";

    private static TimeSpan? FlightDuration(FlightExportRowDto row)
    {
        var duration = row.ScheduledDepartureUtc - row.ScheduledArrivalUtc;
        return duration < TimeSpan.Zero ? null : duration;
    }

    private static string DurationDisplay(FlightExportRowDto row)
    {
        if (FlightDuration(row) is not { } duration)
            return "-";

        var totalMinutes = (int)Math.Round(duration.TotalMinutes);
        var days = totalMinutes / 1_440;
        var hours = totalMinutes % 1_440 / 60;
        var minutes = totalMinutes % 60;

        if (days > 0)
            return minutes > 0 ? $"{days}d {hours}h {minutes}m" : hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        return $"{minutes}m";
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
