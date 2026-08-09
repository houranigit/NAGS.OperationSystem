using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MasterData.Contracts.Resources;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;

namespace Operations.Api.Exports;

internal enum FlightExportFormat
{
    Xlsx,
    Csv,
    Pdf
}

internal sealed record FlightExportCriteria(
    string? Search,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<Guid> CustomerIds,
    Guid? OperationTypeId,
    IReadOnlyList<FlightStatus>? Statuses,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories,
    IReadOnlyList<Guid> ServiceIds,
    bool ToUtcExclusive,
    string? Sort);

internal sealed record FlightExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Canonical presentation-layer document generation shared by the Flights list and operations
/// dashboard. Record selection remains in each authorized application query; this factory only
/// turns the resulting common projection into native files.
/// </summary>
internal static class FlightExportDocumentFactory
{
    private const string ReportTitle = "Daily Operation Report";
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
    private const string PerLandingRowColor = "#FFF3BF";
    private const string CanceledRowColor = "#FADADD";

    private static readonly string[] CsvHeaders =
    [
        "#", "WO#", "Flight#", "WO Flight#", "STA", "STD", "ATA", "ATD",
        "Arrival Delay", "Departure Delay", "Scheduled Duration", "Actual Duration",
        "Customer IATA Code", "Customer Name", "Station IATA Code", "Station Name",
        "Aircraft Manufacturer", "Aircraft Model", "Aircraft Tail Number", "Planned Services",
        "Services", "Tools", "Materials", "General Support", "Assigned Employees", "Remarks", "Status", "Tasks"
    ];

    private static readonly string[] ServiceDetailHeaders =
    [
        "#", "WO#", "WO Status", "Flight#", "WO Flight#", "Flight Status",
        "Customer IATA Code", "Customer Name", "Station IATA Code", "Station Name", "Operation Type",
        "STA", "STD", "ATA", "ATD", "Activity Context", "RTR From", "RTR To", "RTR Description",
        "Service", "From", "To", "Performed By", "Description"
    ];

    private static readonly string[] TaskDetailHeaders =
    [
        "#", "WO#", "WO Status", "Flight#", "WO Flight#", "Flight Status",
        "Customer IATA Code", "Customer Name", "Station IATA Code", "Station Name", "Operation Type",
        "STA", "STD", "ATA", "ATD", "Activity Context", "RTR From", "RTR To", "RTR Description",
        "Major/Minor", "Description", "From", "To", "Performed By", "Tools", "Materials", "General Support"
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
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo? displayTimeZone = null)
    {
        var timeZone = displayTimeZone ?? TimeZoneInfo.Utc;
        var stamp = generatedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture);

        return format switch
        {
            FlightExportFormat.Xlsx => new FlightExportFile(
                CreateWorkbook(rows, criteria, generatedAtUtc, timeZone),
                WorkbookContentType,
                $"flights-report-{stamp}.xlsx"),
            FlightExportFormat.Csv => new FlightExportFile(
                CreateCsv(rows, timeZone),
                CsvContentType,
                $"flights-report-{stamp}.csv"),
            FlightExportFormat.Pdf => new FlightExportFile(
                CreatePdf(rows, criteria, generatedAtUtc, timeZone),
                PdfContentType,
                $"flights-report-{stamp}.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported flight export format.")
        };
    }

    private static byte[] CreateWorkbook(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        var columnCount = CsvHeaders.Length;
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
        SetWorkbookText(
            summaryRange.FirstCell(),
            $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} records  |  Generated {FormatReportTimestamp(generatedAtUtc, timeZone)}");
        summaryRange.Style.Font.FontSize = 10;
        summaryRange.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3E9EA");
        summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 22;

        var filterRange = sheet.Range(3, 1, 3, columnCount).Merge();
        SetWorkbookText(filterRange.FirstCell(), $"Scope: {BuildFilterSummary(rows, criteria, timeZone)}");
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
            WriteWorkbookRow(sheet, rowNumber, sequence++, row, timeZone);

            if ((rowNumber - headerRowNumber) % 2 == 0)
                sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);

            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            sheet.Range(rowNumber, 1, rowNumber, columnCount).Style.Border.BottomBorderColor = XLColor.FromHtml(BorderColor);
            ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 27), row.Status);
            sheet.Row(rowNumber).Height = 20;
            rowNumber++;
        }

        var finalRow = Math.Max(headerRowNumber, rowNumber - 1);
        sheet.Range(headerRowNumber, 1, finalRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRowNumber);

        SetWorkbookColumnWidths(sheet);
        foreach (var column in new[] { 5, 6, 7, 8 })
            sheet.Column(column).Style.DateFormat.Format = WorkbookDateFormat(timeZone);
        foreach (var column in new[] { 9, 10 })
            sheet.Column(column).Style.NumberFormat.Format = "0 \"min\";-0 \"min\"";
        foreach (var column in new[] { 11, 12 })
            sheet.Column(column).Style.NumberFormat.Format = "[h]\"h \"mm\"m\"";

        AddServiceDetailsWorksheet(workbook, rows, criteria, generatedAtUtc, timeZone);
        AddTaskDetailsWorksheet(workbook, rows, criteria, generatedAtUtc, timeZone);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteWorkbookRow(
        IXLWorksheet sheet,
        int rowNumber,
        int sequence,
        FlightExportRowDto row,
        TimeZoneInfo timeZone)
    {
        var approved = row.ApprovedWorkOrder;
        sheet.Cell(rowNumber, 1).SetValue(sequence);
        sheet.Cell(rowNumber, 2).SetValue(SpreadsheetSafeText(approved?.ApprovalNumber ?? "-"));
        sheet.Cell(rowNumber, 3).SetValue(SpreadsheetSafeText(DisplayFlightNumber(row, row.FlightNumber)));
        SetOptionalText(sheet.Cell(rowNumber, 4), approved is null ? null : DisplayFlightNumber(row, approved.ActualFlightNumber));
        sheet.Cell(rowNumber, 5).SetValue(ToWorkbookDate(row.ScheduledArrivalUtc, timeZone));
        sheet.Cell(rowNumber, 6).SetValue(ToWorkbookDate(row.ScheduledDepartureUtc, timeZone));
        SetOptionalDate(sheet.Cell(rowNumber, 7), approved?.ActualArrivalUtc, timeZone);
        SetOptionalDate(sheet.Cell(rowNumber, 8), approved?.ActualDepartureUtc, timeZone);
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
        SetOptionalText(sheet.Cell(rowNumber, 22), approved is null ? null : JoinNames(approved.ToolNames));
        SetOptionalText(sheet.Cell(rowNumber, 23), approved is null ? null : JoinNames(approved.MaterialNames));
        SetOptionalText(sheet.Cell(rowNumber, 24), approved is null ? null : JoinNames(approved.GeneralSupportNames));
        SetOptionalText(sheet.Cell(rowNumber, 25), JoinNames(row.AssignedEmployeeNames));
        SetOptionalText(sheet.Cell(rowNumber, 26), approved?.Remarks);
        sheet.Cell(rowNumber, 27).SetValue(StatusLabel(row.Status));
        SetOptionalText(sheet.Cell(rowNumber, 28), approved is null ? null : JoinNames(approved.TaskNames));

        var rowRange = sheet.Range(rowNumber, 1, rowNumber, CsvHeaders.Length);
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
        var widths = new[]
        {
            7d, 16, 18, 18, 21, 21, 21, 21, 16, 18, 19, 17, 18, 28, 18, 26, 22,
            20, 20, 35, 35, 30, 30, 30, 35, 40, 16, 42
        };
        for (var index = 0; index < widths.Length; index++)
            sheet.Column(index + 1).Width = widths[index];
    }

    private static void AddServiceDetailsWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        const int headerRowNumber = 5;
        var detailCount = rows.Sum(row => row.ApprovedWorkOrder?.ServiceDetails.Count ?? 0);
        var sheet = CreateDetailWorksheetFrame(
            workbook,
            "Service Details",
            "Daily Operation Report — Service Details",
            ServiceDetailHeaders,
            detailCount,
            rows,
            criteria,
            generatedAtUtc,
            timeZone);

        var rowNumber = headerRowNumber + 1;
        var sequence = 1;
        foreach (var flight in rows)
        {
            if (flight.ApprovedWorkOrder is not { } workOrder)
                continue;

            foreach (var detail in workOrder.ServiceDetails)
            {
                WriteDetailIdentity(
                    sheet,
                    rowNumber,
                    sequence++,
                    flight,
                    workOrder,
                    detail.ReturnToRamp,
                    timeZone);
                SetWorkbookText(sheet.Cell(rowNumber, 20), detail.ServiceName);
                SetOptionalDate(sheet.Cell(rowNumber, 21), detail.FromUtc, timeZone);
                SetOptionalDate(sheet.Cell(rowNumber, 22), detail.ToUtc, timeZone);
                SetOptionalText(sheet.Cell(rowNumber, 23), JoinNames(detail.PerformedByNames));
                SetOptionalText(sheet.Cell(rowNumber, 24), detail.Description);
                StyleDetailDataRow(
                    sheet,
                    rowNumber,
                    ServiceDetailHeaders.Length,
                    workOrder.WorkOrderStatus,
                    flight.Status);
                rowNumber++;
            }
        }

        FinalizeDetailWorksheet(
            sheet,
            ServiceDetailHeaders.Length,
            headerRowNumber,
            rowNumber,
            [12, 13, 14, 15, 17, 18, 21, 22],
            [19, 23, 24],
            [
                7d, 17, 15, 17, 17, 15, 18, 27, 18, 27, 20, 20, 20, 20, 20,
                21, 20, 20, 34, 28, 20, 20, 30, 42
            ],
            "No work-order services match the selected flights.",
            timeZone);
    }

    private static void AddTaskDetailsWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        const int headerRowNumber = 5;
        var detailCount = rows.Sum(row => row.ApprovedWorkOrder?.TaskDetails.Count ?? 0);
        var sheet = CreateDetailWorksheetFrame(
            workbook,
            "Task Details",
            "Daily Operation Report — Task Details",
            TaskDetailHeaders,
            detailCount,
            rows,
            criteria,
            generatedAtUtc,
            timeZone);

        var rowNumber = headerRowNumber + 1;
        var sequence = 1;
        foreach (var flight in rows)
        {
            if (flight.ApprovedWorkOrder is not { } workOrder)
                continue;

            foreach (var detail in workOrder.TaskDetails)
            {
                WriteDetailIdentity(
                    sheet,
                    rowNumber,
                    sequence++,
                    flight,
                    workOrder,
                    detail.ReturnToRamp,
                    timeZone);
                SetWorkbookText(sheet.Cell(rowNumber, 20), detail.TaskType);
                SetOptionalText(sheet.Cell(rowNumber, 21), detail.Description);
                SetOptionalDate(sheet.Cell(rowNumber, 22), detail.FromUtc, timeZone);
                SetOptionalDate(sheet.Cell(rowNumber, 23), detail.ToUtc, timeZone);
                SetOptionalText(sheet.Cell(rowNumber, 24), JoinNames(detail.PerformedByNames));
                SetOptionalText(sheet.Cell(rowNumber, 25), JoinResourceUsages(detail.Tools, timeZone));
                SetOptionalText(sheet.Cell(rowNumber, 26), JoinResourceUsages(detail.Materials, timeZone));
                SetOptionalText(sheet.Cell(rowNumber, 27), JoinResourceUsages(detail.GeneralSupports, timeZone));
                StyleDetailDataRow(
                    sheet,
                    rowNumber,
                    TaskDetailHeaders.Length,
                    workOrder.WorkOrderStatus,
                    flight.Status);
                rowNumber++;
            }
        }

        FinalizeDetailWorksheet(
            sheet,
            TaskDetailHeaders.Length,
            headerRowNumber,
            rowNumber,
            [12, 13, 14, 15, 17, 18, 22, 23],
            [19, 21, 24, 25, 26, 27],
            [
                7d, 17, 15, 17, 17, 15, 18, 27, 18, 27, 20, 20, 20, 20, 20,
                21, 20, 20, 34, 15, 42, 20, 20, 30, 34, 34, 34
            ],
            "No work-order tasks match the selected flights.",
            timeZone);
    }

    private static IXLWorksheet CreateDetailWorksheetFrame(
        XLWorkbook workbook,
        string worksheetName,
        string title,
        IReadOnlyList<string> headers,
        int detailCount,
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        const int headerRowNumber = 5;
        var sheet = workbook.Worksheets.Add(worksheetName);
        sheet.ShowGridLines = false;

        var titleRange = sheet.Range(1, 1, 1, headers.Count).Merge();
        SetWorkbookText(titleRange.FirstCell(), title);
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 18;
        titleRange.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandColor);
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Row(1).Height = 34;

        var summaryRange = sheet.Range(2, 1, 2, headers.Count).Merge();
        SetWorkbookText(
            summaryRange.FirstCell(),
            $"{detailCount.ToString("N0", CultureInfo.InvariantCulture)} detail records across " +
            $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} flights  |  " +
            $"Generated {FormatReportTimestamp(generatedAtUtc, timeZone)}");
        summaryRange.Style.Font.FontSize = 10;
        summaryRange.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3E9EA");
        summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 22;

        var filterRange = sheet.Range(3, 1, 3, headers.Count).Merge();
        SetWorkbookText(filterRange.FirstCell(), $"Scope: {BuildFilterSummary(rows, criteria, timeZone)}");
        filterRange.Style.Font.FontSize = 9;
        filterRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        filterRange.Style.Alignment.WrapText = true;
        filterRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(3).Height = 30;

        for (var index = 0; index < headers.Count; index++)
            SetWorkbookText(sheet.Cell(headerRowNumber, index + 1), headers[index]);

        var header = sheet.Range(headerRowNumber, 1, headerRowNumber, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml(HeaderTextColor);
        header.Style.Font.FontSize = 9;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandDarkColor);
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorderColor = XLColor.FromHtml(BrandDarkColor);
        sheet.Row(headerRowNumber).Height = 32;

        return sheet;
    }

    private static void WriteDetailIdentity(
        IXLWorksheet sheet,
        int rowNumber,
        int sequence,
        FlightExportRowDto flight,
        ApprovedWorkOrderExportDto workOrder,
        FlightExportReturnToRampContextDto? returnToRamp,
        TimeZoneInfo timeZone)
    {
        sheet.Cell(rowNumber, 1).SetValue(sequence);
        SetWorkbookText(sheet.Cell(rowNumber, 2), WorkOrderReference(workOrder));
        SetWorkbookText(
            sheet.Cell(rowNumber, 3),
            string.IsNullOrWhiteSpace(workOrder.WorkOrderStatus) ? "-" : workOrder.WorkOrderStatus);
        SetWorkbookText(sheet.Cell(rowNumber, 4), DisplayFlightNumber(flight, flight.FlightNumber));
        SetWorkbookText(sheet.Cell(rowNumber, 5), DisplayFlightNumber(flight, workOrder.ActualFlightNumber));
        SetWorkbookText(sheet.Cell(rowNumber, 6), StatusLabel(flight.Status));
        SetOptionalText(sheet.Cell(rowNumber, 7), flight.CustomerIataCode);
        SetWorkbookText(sheet.Cell(rowNumber, 8), flight.CustomerName);
        SetWorkbookText(sheet.Cell(rowNumber, 9), flight.StationIata);
        SetWorkbookText(sheet.Cell(rowNumber, 10), flight.StationName);
        SetWorkbookText(sheet.Cell(rowNumber, 11), flight.OperationTypeName);
        SetOptionalDate(sheet.Cell(rowNumber, 12), flight.ScheduledArrivalUtc, timeZone);
        SetOptionalDate(sheet.Cell(rowNumber, 13), flight.ScheduledDepartureUtc, timeZone);
        SetOptionalDate(sheet.Cell(rowNumber, 14), workOrder.ActualArrivalUtc, timeZone);
        SetOptionalDate(sheet.Cell(rowNumber, 15), workOrder.ActualDepartureUtc, timeZone);
        SetWorkbookText(
            sheet.Cell(rowNumber, 16),
            returnToRamp is null ? "Work order" : $"Return to ramp #{returnToRamp.Sequence}");
        SetOptionalDate(sheet.Cell(rowNumber, 17), returnToRamp?.FromUtc, timeZone);
        SetOptionalDate(sheet.Cell(rowNumber, 18), returnToRamp?.ToUtc, timeZone);
        SetOptionalText(sheet.Cell(rowNumber, 19), returnToRamp?.Description);

    }

    private static void StyleDetailDataRow(
        IXLWorksheet sheet,
        int rowNumber,
        int columnCount,
        string workOrderStatus,
        string flightStatus)
    {
        var rowRange = sheet.Range(rowNumber, 1, rowNumber, columnCount);
        rowRange.Style.Font.FontSize = 9;
        rowRange.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        rowRange.Style.Border.BottomBorderColor = XLColor.FromHtml(BorderColor);
        if ((rowNumber - 5) % 2 == 0)
            rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);

        ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 3), workOrderStatus);
        ApplyWorkbookStatusStyle(sheet.Cell(rowNumber, 6), flightStatus);
        sheet.Row(rowNumber).Height = 26;
    }

    private static void FinalizeDetailWorksheet(
        IXLWorksheet sheet,
        int columnCount,
        int headerRowNumber,
        int nextRowNumber,
        IReadOnlyList<int> dateColumns,
        IReadOnlyList<int> wrappedColumns,
        IReadOnlyList<double> widths,
        string emptyMessage,
        TimeZoneInfo timeZone)
    {
        var finalDataRow = nextRowNumber - 1;
        var autoFilterLastRow = Math.Max(headerRowNumber, finalDataRow);
        sheet.Range(headerRowNumber, 1, autoFilterLastRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRowNumber);

        for (var index = 0; index < widths.Count; index++)
            sheet.Column(index + 1).Width = widths[index];
        foreach (var column in dateColumns)
            sheet.Column(column).Style.DateFormat.Format = WorkbookDateFormat(timeZone);
        foreach (var column in wrappedColumns)
            sheet.Column(column).Style.Alignment.WrapText = true;

        if (finalDataRow >= headerRowNumber + 1)
            return;

        var emptyRange = sheet.Range(headerRowNumber + 1, 1, headerRowNumber + 1, columnCount).Merge();
        SetWorkbookText(emptyRange.FirstCell(), emptyMessage);
        emptyRange.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        emptyRange.Style.Font.Italic = true;
        emptyRange.Style.Fill.BackgroundColor = XLColor.FromHtml(AlternateRowColor);
        emptyRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        emptyRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(headerRowNumber + 1).Height = 30;
    }

    private static string WorkOrderReference(ApprovedWorkOrderExportDto workOrder)
    {
        if (!string.IsNullOrWhiteSpace(workOrder.ApprovalNumber))
            return workOrder.ApprovalNumber;

        return workOrder.WorkOrderId == Guid.Empty
            ? "-"
            : $"WO-{workOrder.WorkOrderId.ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant()}";
    }

    private static string JoinResourceUsages(
        IReadOnlyList<FlightExportResourceUsageDto> resources,
        TimeZoneInfo timeZone) =>
        string.Join(
            ", ",
            resources.Select(resource => FormatResourceUsage(resource, timeZone)));

    private static string FormatResourceUsage(
        FlightExportResourceUsageDto resource,
        TimeZoneInfo timeZone)
    {
        if (resource.CalculationType == ResourceCalculationType.Quantity)
        {
            return resource.Quantity is { } quantity
                ? $"{resource.Name} × {quantity.ToString("0.##", CultureInfo.InvariantCulture)}"
                : resource.Name;
        }

        if (resource.FromUtc is not { } fromUtc)
            return $"{resource.Name} (duration not recorded)";

        if (IsUtc(timeZone))
        {
            var utcFrom = fromUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            var utcTo = resource.ToUtc is { } utcToValue
                ? utcToValue.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : "open";
            return $"{resource.Name}: {utcFrom} → {utcTo}";
        }

        var from = TimeZoneInfo.ConvertTime(fromUtc, timeZone)
            .ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
        var to = resource.ToUtc is { } toUtc
            ? TimeZoneInfo.ConvertTime(toUtc, timeZone)
                .ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            : "open";
        return $"{resource.Name}: {from} → {to} [{timeZone.Id}]";
    }

    private static void SetWorkbookText(IXLCell cell, string value) =>
        cell.SetValue(SpreadsheetSafeText(value));

    private static byte[] CreateCsv(IReadOnlyList<FlightExportRowDto> rows, TimeZoneInfo timeZone)
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
                    FormatCsvTimestamp(row.ScheduledArrivalUtc, timeZone), FormatCsvTimestamp(row.ScheduledDepartureUtc, timeZone),
                    FormatCsvTimestamp(approved?.ActualArrivalUtc, timeZone), FormatCsvTimestamp(approved?.ActualDepartureUtc, timeZone),
                    FormatCsvDuration(ArrivalDelay(row)), FormatCsvDuration(DepartureDelay(row)),
                    FormatCsvDuration(ScheduledDuration(row)), FormatCsvDuration(ActualDuration(row)),
                    SpreadsheetSafeText(row.CustomerIataCode ?? string.Empty), SpreadsheetSafeText(row.CustomerName),
                    SpreadsheetSafeText(row.StationIata), SpreadsheetSafeText(row.StationName),
                    SpreadsheetSafeText(approved?.AircraftManufacturer ?? string.Empty),
                    SpreadsheetSafeText(approved?.AircraftModel ?? string.Empty),
                    SpreadsheetSafeText(approved?.AircraftTailNumber ?? string.Empty),
                    SpreadsheetSafeText(JoinNames(row.PlannedServiceNames)),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.ServiceNames)),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.ToolNames)),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.MaterialNames)),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.GeneralSupportNames)),
                    SpreadsheetSafeText(JoinNames(row.AssignedEmployeeNames)),
                    SpreadsheetSafeText(approved?.Remarks ?? string.Empty), StatusLabel(row.Status),
                    SpreadsheetSafeText(approved is null ? string.Empty : JoinNames(approved.TaskNames))
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
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        PdfDocumentAssets.EnsureFontResolver();

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

        AddPdfFooter(section, generatedAtUtc, timeZone);
        AddPdfFirstPageHeader(section, rows, criteria, timeZone);

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
        FlightExportCriteria criteria,
        TimeZoneInfo timeZone)
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

        var date = mastheadRow.Cells[2].AddParagraph(PdfSafeText(PdfDateScope(criteria, timeZone)));
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

    private static void AddPdfFooter(
        Section section,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
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
        footer.AddText($"Generated {FormatReportTimestamp(generatedAtUtc, timeZone)}   |   Page ");
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

    internal static IReadOnlyList<PdfColumnSpec> BuildPdfColumns(FlightExportCriteria criteria)
    {
        var columns = new List<PdfColumnSpec>
        {
            new("#", 0.8, ParagraphAlignment.Center, (_, sequence) => sequence.ToString(CultureInfo.InvariantCulture)),
            new("WO#", 2.2, ParagraphAlignment.Center, (row, _) => row.ApprovedWorkOrder?.ApprovalNumber ?? "-"),
            new("Flight#", 2.1, ParagraphAlignment.Left, (row, _) => DisplayFlightNumber(row, row.ApprovedWorkOrder?.ActualFlightNumber ?? row.FlightNumber))
        };
        if (criteria.CustomerIds.Count != 1)
            columns.Add(new("Customer", 5.0, ParagraphAlignment.Left, (row, _) => row.CustomerName));
        if (criteria.StationIds.Count != 1)
            columns.Add(new("Station", 2.5, ParagraphAlignment.Left, (row, _) => row.StationName));

        var reclaimed = (criteria.CustomerIds.Count == 1 ? 2.5 : 0) +
            (criteria.StationIds.Count == 1 ? 1.25 : 0);
        columns.Add(new("Aircraft", 2.4, ParagraphAlignment.Left, (row, _) => row.ApprovedWorkOrder?.AircraftModel ?? string.Empty));
        columns.Add(new("Services", 5.0 + reclaimed, ParagraphAlignment.Left, (row, _) => PdfServices(row)));
        columns.Add(new("Remarks", 5.4 + reclaimed, ParagraphAlignment.Left, (row, _) => row.ApprovedWorkOrder?.Remarks ?? string.Empty));
        return columns;
    }

    internal sealed record PdfColumnSpec(
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

    private static string BuildFilterSummary(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria,
        TimeZoneInfo timeZone)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
            filters.Add($"Search: {criteria.Search.Trim()}");

        if (criteria.StationIds.Count == 1)
        {
            var station = rows.FirstOrDefault();
            filters.Add(station is null ? "Station filter applied" : $"Station: {station.StationIata} - {station.StationName}");
        }
        else if (criteria.StationIds.Count > 1)
        {
            filters.Add($"Stations: {criteria.StationIds.Count} selected");
        }

        if (criteria.CustomerIds.Count == 1)
        {
            var customer = rows.FirstOrDefault();
            filters.Add(customer is null ? "Customer filter applied" : $"Customer: {CustomerDisplay(customer)}");
        }
        else if (criteria.CustomerIds.Count > 1)
        {
            filters.Add($"Customers: {criteria.CustomerIds.Count} selected");
        }

        if (criteria.FromUtc is { } from && DisplayToUtc(criteria) is { } to)
            filters.Add($"Scheduled arrival: {DisplayDate(from, timeZone)} to {DisplayDate(to, timeZone)}{ZoneSuffix(timeZone)}");
        else if (criteria.FromUtc is { } fromOnly)
            filters.Add($"Scheduled arrival from: {DisplayDate(fromOnly, timeZone)}{ZoneSuffix(timeZone)}");
        else if (DisplayToUtc(criteria) is { } toOnly)
            filters.Add($"Scheduled arrival through: {DisplayDate(toOnly, timeZone)}{ZoneSuffix(timeZone)}");

        if (criteria.ServiceCategories is { Count: > 0 } categories)
            filters.Add($"Service category: {string.Join(", ", categories.Select(ServiceCategoryLabel))}");
        if (criteria.ServiceIds.Count > 0)
            filters.Add($"Performed services: {criteria.ServiceIds.Count} selected");

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

    private static void SetOptionalDate(IXLCell cell, DateTimeOffset? value, TimeZoneInfo timeZone)
    {
        if (value.HasValue)
            cell.SetValue(ToWorkbookDate(value.Value, timeZone));
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

    internal static string FormatCsvTimestamp(DateTimeOffset? value, TimeZoneInfo timeZone)
    {
        if (!value.HasValue)
            return string.Empty;

        if (IsUtc(timeZone))
            return value.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var local = TimeZoneInfo.ConvertTime(value.Value, timeZone);
        return $"{local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)} [{timeZone.Id}]";
    }

    private static string FormatCsvDuration(TimeSpan? value) => value.HasValue
        ? Math.Round(value.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    internal static string PdfDateScope(FlightExportCriteria criteria, TimeZoneInfo timeZone)
    {
        if (!IsUtc(timeZone))
        {
            if (criteria.FromUtc is { } localFrom && DisplayToUtc(criteria) is { } localTo)
                return $"From {FormatPdfTimestamp(localFrom, timeZone)}\nTo   {FormatPdfTimestamp(localTo, timeZone)}\nZone {timeZone.Id}";
            if (criteria.FromUtc is { } localFromOnly)
                return $"From {FormatPdfTimestamp(localFromOnly, timeZone)}\nZone {timeZone.Id}";
            if (DisplayToUtc(criteria) is { } localToOnly)
                return $"To   {FormatPdfTimestamp(localToOnly, timeZone)}\nZone {timeZone.Id}";
            return string.Empty;
        }

        if (criteria.FromUtc is { } from && DisplayToUtc(criteria) is { } to)
            return $"From {from.UtcDateTime:dd MMM yyyy HH:mm} UTC\nTo   {to.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        if (criteria.FromUtc is { } fromOnly)
            return $"From {fromOnly.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        if (DisplayToUtc(criteria) is { } toOnly)
            return $"To   {toOnly.UtcDateTime:dd MMM yyyy HH:mm} UTC";
        return string.Empty;
    }

    private static DateTimeOffset? DisplayToUtc(FlightExportCriteria criteria) =>
        criteria is { ToUtcExclusive: true, ToUtc: { } exclusiveTo } &&
        exclusiveTo != DateTimeOffset.MinValue
            ? exclusiveTo.AddTicks(-1)
            : criteria.ToUtc;

    private static IReadOnlyList<string> BuildPdfScopeLines(
        IReadOnlyList<FlightExportRowDto> rows,
        FlightExportCriteria criteria)
    {
        var lines = new List<string>();
        if (criteria.CustomerIds.Count == 1)
            lines.Add(rows.FirstOrDefault() is { } row ? $"Customer: {CustomerDisplay(row)}" : "Customer filter applied");
        else if (criteria.CustomerIds.Count > 1)
            lines.Add($"Customers: {criteria.CustomerIds.Count} selected");
        if (criteria.StationIds.Count == 1)
            lines.Add(rows.FirstOrDefault() is { } row ? $"Station: {row.StationIata} - {row.StationName}" : "Station filter applied");
        else if (criteria.StationIds.Count > 1)
            lines.Add($"Stations: {criteria.StationIds.Count} selected");
        if (criteria.ServiceIds.Count > 0)
            lines.Add($"Performed services: {criteria.ServiceIds.Count} selected");
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

    internal static string FormatReportTimestamp(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        if (IsUtc(timeZone))
            return value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return $"{local.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)} [{timeZone.Id}]";
    }

    private static string FormatPdfTimestamp(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone)
            .ToString("dd MMM yyyy HH:mm zzz", CultureInfo.InvariantCulture);

    private static DateTime ToWorkbookDate(DateTimeOffset value, TimeZoneInfo timeZone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(value, timeZone).DateTime, DateTimeKind.Unspecified);

    private static string WorkbookDateFormat(TimeZoneInfo timeZone)
    {
        var label = timeZone.Id.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"yyyy-mm-dd hh:mm \"{label}\"";
    }

    private static string DisplayDate(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ZoneSuffix(TimeZoneInfo timeZone) =>
        IsUtc(timeZone) ? string.Empty : $" [{timeZone.Id}]";

    private static bool IsUtc(TimeZoneInfo timeZone) =>
        timeZone.Equals(TimeZoneInfo.Utc) || string.Equals(timeZone.Id, TimeZoneInfo.Utc.Id, StringComparison.Ordinal);

}
