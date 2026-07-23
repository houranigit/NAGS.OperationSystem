using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Operations.Application.Contracts;

namespace Operations.Api.Exports;

internal enum DashboardFlightExportFormat
{
    Xlsx,
    Csv,
    Pdf
}

internal sealed record DashboardFlightExportCriteria(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<Guid> CustomerIds,
    IReadOnlyList<Guid> ServiceIds);

internal sealed record DashboardFlightExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Generates the deliberately narrow dashboard export. Its columns mirror the read-only dashboard
/// table and do not expose the broader operational report fields.
/// </summary>
internal static class DashboardFlightExportDocumentFactory
{
    private const string ReportTitle = "Operations Dashboard Flight Report";
    private const string CompanyName = "National Aviation Ground Support";
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const string PdfContentType = "application/pdf";
    private const string FontFamily = PdfDocumentAssets.FontFamily;

    private const string BrandColor = "#7A3038";
    private const string BrandDarkColor = "#562128";
    private const string HeaderTextColor = "#FFFFFF";
    private const string TextColor = "#1F2937";
    private const string MutedTextColor = "#64748B";
    private const string BorderColor = "#D7DEE8";
    private const string AlternateRowColor = "#F6F8FB";
    private const string CanceledRowColor = "#FADADD";

    private const int ColumnCount = 7;
    private const int HeaderRowNumber = 5;

    private static readonly string[] Headers =
    [
        "#",
        "Flight / STA (UTC)",
        "Customer code",
        "Station code",
        "Operation type",
        "Performed services",
        "Status"
    ];

    public static bool TryParseFormat(string? value, out DashboardFlightExportFormat format)
    {
        if (string.Equals(value, "xlsx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "excel", StringComparison.OrdinalIgnoreCase))
        {
            format = DashboardFlightExportFormat.Xlsx;
            return true;
        }

        if (string.Equals(value, "csv", StringComparison.OrdinalIgnoreCase))
        {
            format = DashboardFlightExportFormat.Csv;
            return true;
        }

        if (string.Equals(value, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            format = DashboardFlightExportFormat.Pdf;
            return true;
        }

        format = default;
        return false;
    }

    public static DashboardFlightExportFile Create(
        DashboardFlightExportFormat format,
        IReadOnlyList<DashboardFlightRowDto> rows,
        DashboardFlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        var stamp = generatedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture);
        return format switch
        {
            DashboardFlightExportFormat.Xlsx => new DashboardFlightExportFile(
                CreateWorkbook(rows, criteria, generatedAtUtc),
                WorkbookContentType,
                $"operations-dashboard-flights-{stamp}.xlsx"),
            DashboardFlightExportFormat.Csv => new DashboardFlightExportFile(
                CreateCsv(rows),
                CsvContentType,
                $"operations-dashboard-flights-{stamp}.csv"),
            DashboardFlightExportFormat.Pdf => new DashboardFlightExportFile(
                CreatePdf(rows, criteria, generatedAtUtc),
                PdfContentType,
                $"operations-dashboard-flights-{stamp}.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported dashboard export format.")
        };
    }

    private static byte[] CreateWorkbook(
        IReadOnlyList<DashboardFlightRowDto> rows,
        DashboardFlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = ReportTitle;
        workbook.Properties.Subject = "Filtered operations dashboard flight data";
        workbook.Properties.Author = "Operations System";
        workbook.Properties.Company = "Operations System";
        workbook.Properties.Created = generatedAtUtc.UtcDateTime;

        var sheet = workbook.Worksheets.Add("Dashboard Flights");
        sheet.ShowGridLines = false;

        var titleRange = sheet.Range(1, 1, 1, ColumnCount).Merge();
        titleRange.Value = ReportTitle;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 18;
        titleRange.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandColor);
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Row(1).Height = 34;

        var summaryRange = sheet.Range(2, 1, 2, ColumnCount).Merge();
        summaryRange.Value =
            $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} records  |  Generated {FormatReportTimestamp(generatedAtUtc)}";
        summaryRange.Style.Font.FontSize = 10;
        summaryRange.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3E9EA");
        summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 22;

        var filterRange = sheet.Range(3, 1, 3, ColumnCount).Merge();
        filterRange.Value = $"Scope: {BuildFilterSummary(rows, criteria)}";
        filterRange.Style.Font.FontSize = 9;
        filterRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        filterRange.Style.Alignment.WrapText = true;
        filterRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(3).Height = 30;

        for (var index = 0; index < Headers.Length; index++)
            sheet.Cell(HeaderRowNumber, index + 1).SetValue(Headers[index]);

        var header = sheet.Range(HeaderRowNumber, 1, HeaderRowNumber, ColumnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        header.Style.Font.FontSize = 9;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandDarkColor);
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorderColor = XLColor.FromHtml(BrandDarkColor);
        sheet.Row(HeaderRowNumber).Height = 30;

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = HeaderRowNumber + index + 1;
            var row = rows[index];
            sheet.Cell(rowNumber, 1).SetValue(index + 1);
            sheet.Cell(rowNumber, 2).SetValue(SpreadsheetSafeText(FlightCellText(row, inline: false)));
            sheet.Cell(rowNumber, 3).SetValue(SpreadsheetSafeText(CustomerCode(row)));
            sheet.Cell(rowNumber, 4).SetValue(SpreadsheetSafeText(row.StationIata));
            sheet.Cell(rowNumber, 5).SetValue(SpreadsheetSafeText(row.OperationTypeName));
            sheet.Cell(rowNumber, 6).SetValue(SpreadsheetSafeText(JoinNames(row.PerformedServiceNames)));
            sheet.Cell(rowNumber, 7).SetValue(StatusLabel(row.Status));

            var rowRange = sheet.Range(rowNumber, 1, rowNumber, ColumnCount);
            if (index % 2 == 1)
                rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);

            rowRange.Style.Font.FontSize = 9;
            rowRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            rowRange.Style.Border.BottomBorderColor = XLColor.FromHtml(BorderColor);
            sheet.Cell(rowNumber, 2).Style.Alignment.WrapText = true;
            sheet.Cell(rowNumber, 6).Style.Alignment.WrapText = true;
            ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 7), row.Status);
            sheet.Row(rowNumber).Height = 32;
        }

        var finalRow = Math.Max(HeaderRowNumber, HeaderRowNumber + rows.Count);
        sheet.Range(HeaderRowNumber, 1, finalRow, ColumnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRowNumber);

        var widths = new[] { 7d, 29, 17, 16, 24, 42, 17 };
        for (var index = 0; index < widths.Length; index++)
            sheet.Column(index + 1).Width = widths[index];

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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

    private static byte[] CreateCsv(IReadOnlyList<DashboardFlightRowDto> rows)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetPreamble());

        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true))
        {
            WriteCsvRow(writer, Headers);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                WriteCsvRow(writer,
                [
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    SpreadsheetSafeText(FlightCellText(row, inline: true)),
                    SpreadsheetSafeText(CustomerCode(row)),
                    SpreadsheetSafeText(row.StationIata),
                    SpreadsheetSafeText(row.OperationTypeName),
                    SpreadsheetSafeText(JoinNames(row.PerformedServiceNames)),
                    SpreadsheetSafeText(StatusLabel(row.Status))
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

            var value = fields[index] ?? string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
            {
                writer.Write('"');
                writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else
            {
                writer.Write(value);
            }
        }

        writer.Write("\r\n");
    }

    private static byte[] CreatePdf(
        IReadOnlyList<DashboardFlightRowDto> rows,
        DashboardFlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc)
    {
        PdfDocumentAssets.EnsureFontResolver();

        var document = new Document();
        document.Info.Title = ReportTitle;
        document.Info.Subject = "Filtered operations dashboard flight data";
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
            AddPdfTable(section, rows);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void AddPdfFirstPageHeader(
        Section section,
        IReadOnlyList<DashboardFlightRowDto> rows,
        DashboardFlightExportCriteria criteria)
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

        var date = mastheadRow.Cells[2].AddParagraph(PdfSafeText(BuildDateScope(criteria)));
        date.Format.Alignment = ParagraphAlignment.Right;
        date.Format.Font.Size = Unit.FromPoint(8);
        date.Format.Font.Bold = true;
        date.Format.Font.Color = Color.Parse(TextColor);

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
        scope.AddFormattedText(
            PdfSafeText($"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} records  |  {BuildFilterSummary(rows, criteria)}"),
            TextFormat.Bold);

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
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

    private static void AddPdfTable(Section section, IReadOnlyList<DashboardFlightRowDto> rows)
    {
        var columns = new[]
        {
            new PdfColumnSpec("#", 0.8, ParagraphAlignment.Center),
            new PdfColumnSpec("Flight / STA (UTC)", 4.3, ParagraphAlignment.Left),
            new PdfColumnSpec("Customer", 2.1, ParagraphAlignment.Center),
            new PdfColumnSpec("Station", 1.9, ParagraphAlignment.Center),
            new PdfColumnSpec("Operation", 3.2, ParagraphAlignment.Left),
            new PdfColumnSpec("Performed services", 10.7, ParagraphAlignment.Left),
            new PdfColumnSpec("Status", 2.4, ParagraphAlignment.Center)
        };

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
        for (var index = 0; index < columns.Length; index++)
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
            else if (index % 2 == 1)
                row.Shading.Color = Color.Parse(AlternateRowColor);

            AddPdfCell(row.Cells[0], (index + 1).ToString(CultureInfo.InvariantCulture), bold: true);
            AddPdfCell(row.Cells[1], FlightCellText(flight, inline: false), bold: true);
            AddPdfCell(row.Cells[2], CustomerCode(flight), bold: true);
            AddPdfCell(row.Cells[3], flight.StationIata, bold: true);
            AddPdfCell(row.Cells[4], flight.OperationTypeName);
            AddPdfCell(row.Cells[5], JoinNames(flight.PerformedServiceNames));
            AddPdfCell(row.Cells[6], StatusLabel(flight.Status), bold: true, color: PdfStatusColor(flight.Status));
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

    private static string BuildFilterSummary(
        IReadOnlyList<DashboardFlightRowDto> rows,
        DashboardFlightExportCriteria criteria)
    {
        var filters = new List<string>();
        if (criteria.FromUtc is { } from && criteria.ToUtc is { } to)
        {
            var inclusiveTo = InclusiveEnd(to);
            filters.Add(from.UtcDateTime.Date == inclusiveTo.UtcDateTime.Date
                ? $"Date: {from.UtcDateTime:dd MMM yyyy} UTC"
                : $"Dates: {from.UtcDateTime:dd MMM yyyy} - {inclusiveTo.UtcDateTime:dd MMM yyyy} UTC");
        }
        else if (criteria.FromUtc is { } fromOnly)
        {
            filters.Add($"From: {fromOnly.UtcDateTime:dd MMM yyyy HH:mm} UTC");
        }
        else if (criteria.ToUtc is { } toOnly)
        {
            filters.Add($"Through: {InclusiveEnd(toOnly).UtcDateTime:dd MMM yyyy HH:mm} UTC");
        }

        if (criteria.StationIds.Count == 1)
        {
            var station = rows.FirstOrDefault(row => row.StationId == criteria.StationIds[0]);
            filters.Add(station is null
                ? "Station filter applied"
                : $"Station: {station.StationIata} - {station.StationName}");
        }
        else if (criteria.StationIds.Count > 1)
        {
            filters.Add($"Stations: {criteria.StationIds.Count} selected");
        }

        if (criteria.CustomerIds.Count == 1)
        {
            var customer = rows.FirstOrDefault();
            filters.Add(customer is null
                ? "Customer filter applied"
                : $"Customer: {CustomerCode(customer)} - {customer.CustomerName}");
        }
        else if (criteria.CustomerIds.Count > 1)
        {
            filters.Add($"Customers: {criteria.CustomerIds.Count} selected");
        }

        if (criteria.ServiceIds.Count > 0)
            filters.Add($"Performed services: {criteria.ServiceIds.Count} selected");

        return filters.Count == 0
            ? "All flights within your authorized scope"
            : string.Join("  |  ", filters);
    }

    private static string BuildDateScope(DashboardFlightExportCriteria criteria)
    {
        if (criteria.FromUtc is { } from && criteria.ToUtc is { } to)
        {
            var inclusiveTo = InclusiveEnd(to);
            return from.UtcDateTime.Date == inclusiveTo.UtcDateTime.Date
                ? $"{from.UtcDateTime:dd MMM yyyy}\nUTC"
                : $"{from.UtcDateTime:dd MMM yyyy}\nto {inclusiveTo.UtcDateTime:dd MMM yyyy} UTC";
        }

        if (criteria.FromUtc is { } fromOnly)
            return $"From {fromOnly.UtcDateTime:dd MMM yyyy HH:mm}\nUTC";
        if (criteria.ToUtc is { } toOnly)
            return $"Through {InclusiveEnd(toOnly).UtcDateTime:dd MMM yyyy HH:mm}\nUTC";
        return string.Empty;
    }

    private static DateTimeOffset InclusiveEnd(DateTimeOffset exclusiveEnd) =>
        exclusiveEnd == DateTimeOffset.MinValue ? exclusiveEnd : exclusiveEnd.AddTicks(-1);

    private static string FlightCellText(DashboardFlightRowDto row, bool inline)
    {
        var separator = inline ? " | " : "\n";
        return $"{DisplayFlightNumber(row)}{separator}STA {FormatFlightTimestamp(row.ScheduledArrivalUtc)} UTC";
    }

    private static string DisplayFlightNumber(DashboardFlightRowDto row) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? row.FlightNumber
            : $"{row.CustomerIataCode.Trim().ToUpperInvariant()}-{row.FlightNumber}";

    private static string CustomerCode(DashboardFlightRowDto row) =>
        string.IsNullOrWhiteSpace(row.CustomerIataCode)
            ? "-"
            : row.CustomerIataCode.Trim().ToUpperInvariant();

    private static string JoinNames(IReadOnlyList<string> names) => string.Join(", ", names);

    private static string StatusLabel(string status) => status switch
    {
        "InProgress" => "In progress",
        _ => status
    };

    private static Color PdfStatusColor(string status) => status switch
    {
        "Completed" => Color.Parse("#18733A"),
        "Canceled" or "Merged" => Color.Parse("#A32632"),
        "InProgress" => Color.Parse("#9A5800"),
        _ => Color.Parse("#536274")
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

    private static string GetLogoDataUri()
    {
        using var stream = PdfDocumentAssets.OpenEmbeddedResource("Operations.Api.Assets.NagsLogo.png");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return "base64:" + Convert.ToBase64String(memory.ToArray());
    }

    private static string FormatReportTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatFlightTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    private sealed record PdfColumnSpec(
        string Header,
        double WidthCentimeters,
        ParagraphAlignment Alignment);
}
