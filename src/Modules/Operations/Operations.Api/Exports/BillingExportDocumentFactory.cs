using System.Globalization;
using ClosedXML.Excel;
using Operations.Application.Contracts;

namespace Operations.Api.Exports;

/// <summary>
/// Flat billing extract: each recorded service, resource usage, or performer has its own row.
/// Selection and authorization are shared with the Flights export query.
/// </summary>
internal static class BillingExportDocumentFactory
{
    private static readonly string[] Headers =
    [
        "WO ID", "Customer", "Station", "AC TYPE", "AC REG. NO", "Flight No",
        "STA", "STA", "ATA", "ATA", "STD", "STD", "ATD", "ATD",
        "Area", "Description", "Start Date", "Start Time", "End Date", "End Time"
    ];

    public static FlightExportFile Create(
        IReadOnlyList<FlightExportRowDto> flights,
        DateTimeOffset generatedAtUtc,
        TimeZoneInfo timeZone)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Billing Report";
        workbook.Properties.Author = "Operations System";
        workbook.Properties.Subject = $"Flight billing activities. Time zone: {timeZone.Id}";
        workbook.Properties.Created = generatedAtUtc.UtcDateTime;
        var sheet = workbook.Worksheets.Add("Billing Report");
        sheet.Style.Font.FontName = "Aptos Narrow";
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        for (var index = 0; index < Headers.Length; index++)
            sheet.Cell(1, index + 1).SetValue(Headers[index]);
        sheet.Range(1, 1, 1, Headers.Length).Style.Font.Bold = true;
        sheet.Row(1).Height = 28;

        var rowNumber = 2;
        foreach (var flight in flights)
        {
            if (flight.ApprovedWorkOrder is not { } workOrder)
                continue;

            foreach (var activity in Activities(workOrder))
            {
                SetText(sheet.Cell(rowNumber, 1), workOrder.ApprovalNumber);
                SetText(sheet.Cell(rowNumber, 2), flight.CustomerName);
                SetText(sheet.Cell(rowNumber, 3), flight.StationName);
                SetText(sheet.Cell(rowNumber, 4), string.Join(" ",
                    new[] { workOrder.AircraftManufacturer, workOrder.AircraftModel }
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())));
                SetText(sheet.Cell(rowNumber, 5), workOrder.AircraftTailNumber);
                SetText(sheet.Cell(rowNumber, 6), workOrder.ActualFlightNumber);
                SetDateAndTime(sheet, rowNumber, 7, flight.ScheduledArrivalUtc, timeZone);
                SetDateAndTime(sheet, rowNumber, 9, workOrder.ActualArrivalUtc, timeZone);
                SetDateAndTime(sheet, rowNumber, 11, flight.ScheduledDepartureUtc, timeZone);
                SetDateAndTime(sheet, rowNumber, 13, workOrder.ActualDepartureUtc, timeZone);
                SetText(sheet.Cell(rowNumber, 15), activity.Area);
                SetText(sheet.Cell(rowNumber, 16), activity.Description);
                SetDateAndTime(sheet, rowNumber, 17, activity.FromUtc, timeZone);
                SetDateAndTime(sheet, rowNumber, 19, activity.ToUtc, timeZone);
                rowNumber++;
            }
        }

        sheet.Columns(1, Headers.Length).Width = 16;
        sheet.Column(2).Width = 32;
        sheet.Column(3).Width = 28;
        sheet.Column(4).Width = 26;
        sheet.Column(15).Width = 20;
        sheet.Column(16).Width = 50;
        sheet.Column(16).Style.Alignment.WrapText = true;
        foreach (var dateColumn in new[] { 7, 9, 11, 13, 17, 19 })
        {
            sheet.Column(dateColumn).Width = 14;
            sheet.Column(dateColumn).Style.NumberFormat.Format = "yyyy/mm/dd";
            sheet.Column(dateColumn + 1).Width = 12;
            sheet.Column(dateColumn + 1).Style.NumberFormat.Format = "hh:mm";
        }

        sheet.Range(1, 1, Math.Max(1, rowNumber - 1), Headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
        sheet.PageSetup.Footer.Right.AddText($"Time zone: {timeZone.Id}");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = TimeZoneInfo.ConvertTime(generatedAtUtc, timeZone)
            .ToString("yyyyMMdd-HHmmsszzz", CultureInfo.InvariantCulture)
            .Replace(":", string.Empty, StringComparison.Ordinal);
        return new FlightExportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"billing-report-{stamp}.xlsx");
    }

    private static IEnumerable<BillingActivity> Activities(ApprovedWorkOrderExportDto workOrder)
    {
        foreach (var service in workOrder.ServiceDetails)
            yield return new("Service", service.ServiceName, service.FromUtc, service.ToUtc);

        foreach (var task in workOrder.TaskDetails)
        {
            foreach (var tool in task.Tools)
                yield return new("Tool", tool.Name, tool.FromUtc, tool.ToUtc);
            foreach (var material in task.Materials)
                yield return new("Material", material.Name, material.FromUtc, material.ToUtc);
            foreach (var support in task.GeneralSupports)
                yield return new("General Support", support.Name, support.FromUtc, support.ToUtc);
        }

        foreach (var manpower in workOrder.ManpowerDetails)
            yield return new("Manpower", manpower.Name, manpower.FromUtc, manpower.ToUtc);
    }

    private static void SetDateAndTime(
        IXLWorksheet sheet, int row, int dateColumn, DateTimeOffset? value, TimeZoneInfo timeZone)
    {
        if (value is null || value == DateTimeOffset.MinValue)
            return;

        var local = TimeZoneInfo.ConvertTime(value.Value, timeZone);
        sheet.Cell(row, dateColumn).SetValue(local.Date);
        sheet.Cell(row, dateColumn + 1).SetValue(local.TimeOfDay);
    }

    private static void SetText(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        // Text identifiers and descriptions must never become spreadsheet formulas.
        cell.SetValue(value);
        cell.Style.IncludeQuotePrefix = true;
    }

    private sealed record BillingActivity(
        string Area, string Description, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);
}
