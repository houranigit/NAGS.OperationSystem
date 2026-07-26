using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Operations.Application.Contracts;
using PdfSharp.Drawing;

namespace Operations.Api.Exports;

internal sealed record WorkOrderPrintFile(byte[] Content, string FileName);

/// <summary>
/// Creates a native, flow-layout A4 record for an approved flight work order. The document keeps
/// planned flight data distinct from work actually performed and paginates every operational list
/// instead of compressing it into the fixed rows of the historic paper form.
/// </summary>
internal static class WorkOrderPrintDocumentFactory
{
    private const string LogoResource = "Operations.Api.Assets.NagsLogo.png";
    private const string FontFamily = PdfDocumentAssets.FontFamily;
    private const double ContentWidthCentimeters = 18.2;

    private const string BrandColor = "#722F37";
    private const string BrandDarkColor = "#4A1F26";
    private const string BrandSoftColor = "#F8EAEC";
    private const string TextColor = "#1F2937";
    private const string MutedTextColor = "#64748B";
    private const string BorderColor = "#D7DEE8";
    private const string HeaderFillColor = "#F3F4F6";
    private const string ColumnHeaderColor = "#E5E7EB";
    private const string AlternateRowColor = "#F8FAFC";
    private const string WhiteColor = "#FFFFFF";

    public static WorkOrderPrintFile Create(ApprovedWorkOrderPrintDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfDocumentAssets.EnsureFontResolver();

        var document = BuildDocument(source);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var output = new MemoryStream();
        renderer.Save(output, closeStream: false);
        return new WorkOrderPrintFile(output.ToArray(), BuildFileName(source.WorkOrder.ApprovalNumber));
    }

    private static Document BuildDocument(ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var document = new Document();
        document.Info.Title = $"Work Order {DisplayValue(workOrder.ApprovalNumber)}";
        document.Info.Subject = "Approved flight work order";
        document.Info.Author = "National Aviation Ground Support";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontFamily;
        normal.Font.Size = Unit.FromPoint(8);
        normal.Font.Color = Color.Parse(TextColor);
        normal.ParagraphFormat.SpaceAfter = Unit.Zero;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.75);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.45);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.4);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.4);
        section.PageSetup.OddAndEvenPagesHeaderFooter = false;
        section.PageSetup.DifferentFirstPageHeaderFooter = false;
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(0.45);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.45);

        AddDocumentHeader(section.Headers.Primary, source);
        AddDocumentFooter(section.Footers.Primary, workOrder);
        AddFlightOverview(section, source);
        AddFlightTimes(section, workOrder);
        AddPlannedFlightServices(section, source);
        AddPerformedServices(section, workOrder);
        AddReturnToRamp(section, workOrder);
        AddWorkOrderSummary(section, source);

        AddRemarks(section, workOrder);
        AddTableSpacing(section);
        AddCorrectiveActions(section, source);
        AddResourceRegister(
            section,
            "Materials Used",
            BuildResourceLines(workOrder, ResourceKind.Material));
        AddResourceRegister(
            section,
            "Tools Used",
            BuildResourceLines(workOrder, ResourceKind.Tool));
        AddResourceRegister(
            section,
            "General Support",
            BuildResourceLines(workOrder, ResourceKind.GeneralSupport));
        AddStaffUtilization(section, source);
        AddAttachmentRegister(section, workOrder);
        AddCustomerAcceptance(section, source);
        AddDocumentControl(section, workOrder);

        return document;
    }

    private static void AddDocumentHeader(
        HeaderFooter header,
        ApprovedWorkOrderPrintDto source)
    {
        var table = ConfigureTable(
            header.AddTable(),
            2.2,
            10.5,
            2.2,
            3.3);
        table.Borders.Width = Unit.Zero;
        table.Borders.Bottom.Color = Color.Parse(BrandColor);
        table.Borders.Bottom.Width = Unit.FromPoint(1.2);

        var first = table.AddRow();
        var second = table.AddRow();
        first.TopPadding = Unit.FromPoint(2);
        first.BottomPadding = Unit.FromPoint(2);
        second.TopPadding = Unit.FromPoint(2);
        second.BottomPadding = Unit.FromPoint(3);

        first.Cells[0].MergeDown = 1;
        first.Cells[1].MergeDown = 1;
        first.Cells[0].VerticalAlignment = VerticalAlignment.Center;
        first.Cells[1].VerticalAlignment = VerticalAlignment.Center;

        var logo = first.Cells[0].AddImage(GetLogoDataUri());
        logo.LockAspectRatio = true;
        logo.Height = Unit.FromCentimeter(1.25);

        var title = first.Cells[1].AddParagraph("WORK ORDER");
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = Unit.FromPoint(19);
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.Parse(TextColor);

        var subtitle = first.Cells[1].AddParagraph("FLIGHT MAINTENANCE AND GROUND SUPPORT");
        subtitle.Format.Alignment = ParagraphAlignment.Center;
        subtitle.Format.Font.Size = Unit.FromPoint(7.2);
        subtitle.Format.Font.Color = Color.Parse(MutedTextColor);
        subtitle.Format.SpaceBefore = Unit.FromPoint(1);

        var headerDetails = BuildHeaderDetails(source.WorkOrder);
        AddHeaderDetail(first.Cells[2], first.Cells[3], headerDetails[0]);
        AddHeaderDetail(second.Cells[2], second.Cells[3], headerDetails[1]);
    }

    private static void AddHeaderDetail(Cell labelCell, Cell valueCell, HeaderDetail detail)
    {
        foreach (var cell in new[] { labelCell, valueCell })
        {
            cell.Borders.Color = Color.Parse(BorderColor);
            cell.Borders.Width = Unit.FromPoint(0.5);
            cell.Shading.Color = Color.Parse(HeaderFillColor);
            cell.VerticalAlignment = VerticalAlignment.Center;
        }

        var label = labelCell.AddParagraph(detail.Label);
        label.Format.Font.Size = Unit.FromPoint(6.2);
        label.Format.Font.Color = Color.Parse(MutedTextColor);
        label.Format.Alignment = ParagraphAlignment.Left;

        var value = valueCell.AddParagraph(PdfSafeText(detail.Value));
        value.Format.Font.Size = Unit.FromPoint(8.2);
        value.Format.Font.Bold = true;
        value.Format.Font.Color = Color.Parse(TextColor);
        value.Format.Alignment = ParagraphAlignment.Right;
    }

    internal static IReadOnlyList<HeaderDetail> BuildHeaderDetails(WorkOrderDetailDto workOrder) =>
    [
        new("WO NUMBER", DisplayValue(workOrder.ApprovalNumber)),
        new("STATION", DisplayValue(workOrder.StationIata))
    ];

    private static void AddDocumentFooter(HeaderFooter footer, WorkOrderDetailDto workOrder)
    {
        var table = ConfigureTable(footer.AddTable(), 6.0, 6.2, 6.0);
        table.Borders.Width = Unit.Zero;
        table.Borders.Top.Color = Color.Parse(BorderColor);
        table.Borders.Top.Width = Unit.FromPoint(0.5);
        table.Format.Font.Name = FontFamily;
        table.Format.Font.Size = Unit.FromPoint(6.5);
        table.Format.Font.Color = Color.Parse(MutedTextColor);

        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(4);
        var left = row.Cells[0].AddParagraph($"WO {DisplayValue(workOrder.ApprovalNumber)}");
        left.Format.Alignment = ParagraphAlignment.Left;

        var center = row.Cells[1].AddParagraph("CONTROLLED RECORD  |  ALL TIMES UTC");
        center.Format.Alignment = ParagraphAlignment.Center;

        var right = row.Cells[2].AddParagraph();
        right.Format.Alignment = ParagraphAlignment.Right;
        right.AddText("Page ");
        right.AddPageField();
        right.AddText(" of ");
        right.AddNumPagesField();
    }

    private static void AddFlightOverview(Section section, ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var table = CreateContentTable(section, 7.0, 5.4, 5.8);
        table.KeepTogether = true;
        AddSectionHeaderRow(table, "Flight Overview");

        var first = AddFactRow(table);
        AddFactCell(
            first.Cells[0],
            "Customer / Airline",
            workOrder.CustomerName,
            string.IsNullOrWhiteSpace(workOrder.CustomerIataCode)
                ? null
                : $"IATA {workOrder.CustomerIataCode!.Trim().ToUpperInvariant()}");
        AddFactCell(
            first.Cells[1],
            "Actual Flight",
            DisplayFlightNumber(workOrder, workOrder.ActualFlightNumber));
        AddFactCell(first.Cells[2], "Operation Type", workOrder.OperationTypeName);

        var second = AddFactRow(table);
        AddFactCell(
            second.Cells[0],
            "Aircraft",
            JoinNonEmpty(source.AircraftManufacturer, workOrder.AircraftTypeModel),
            BuildScheduledAircraftNote(source.Flight));
        AddFactCell(second.Cells[1], "Registration", DisplayValue(workOrder.AircraftTailNumber));
        AddFactCell(
            second.Cells[2],
            "Station",
            JoinCodeAndName(workOrder.StationIata, workOrder.StationName));

        var third = AddFactRow(table);
        AddFactCell(third.Cells[0], "Contract", DisplayValue(source.ContractNumber));
        AddFactCell(
            third.Cells[1],
            "Scheduled Flight",
            DisplayFlightNumber(workOrder, source.Flight.CurrentFlightNumber),
            BuildOriginalFlightNote(source));
        AddFactCell(
            third.Cells[2],
            "Service Basis",
            source.Flight.IsPerLanding ? "Per Landing" : "Standard flight");

        KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static string? BuildScheduledAircraftNote(WorkOrderPrintFlightDto flight)
    {
        var scheduled = JoinNonEmpty(flight.ScheduledAircraftManufacturer, flight.ScheduledAircraftModel);
        return scheduled.Length == 0 ? null : $"Scheduled: {scheduled}";
    }

    private static string? BuildOriginalFlightNote(ApprovedWorkOrderPrintDto source)
    {
        var original = Clean(source.Flight.OriginalFlightNumber);
        if (original.Length == 0 ||
            original.Equals(source.Flight.CurrentFlightNumber, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"Original: {DisplayFlightNumber(source.WorkOrder, original)}";
    }

    private static void AddFlightTimes(Section section, WorkOrderDetailDto workOrder)
    {
        var table = CreateContentTable(section, 2.2, 5.1, 5.1, 5.8);
        table.KeepTogether = true;
        AddSectionHeaderRow(table, "Flight Schedule and Actual Times");
        AddColumnHeaderRow(table, "EVENT", "SCHEDULED", "ACTUAL", "VARIANCE");

        AddTimeRow(
            table,
            "Arrival",
            workOrder.ScheduledArrivalUtc,
            workOrder.ActualArrivalUtc);
        AddTimeRow(
            table,
            "Departure",
            workOrder.ScheduledDepartureUtc,
            workOrder.ActualDepartureUtc);

        KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddTimeRow(
        Table table,
        string eventName,
        DateTimeOffset scheduled,
        DateTimeOffset? actual)
    {
        var row = AddDataRow(table);
        AddCellText(row.Cells[0], eventName, bold: true);
        AddCellText(row.Cells[1], FormatTimestamp(scheduled));
        AddCellText(row.Cells[2], FormatTimestamp(actual));
        AddCellText(
            row.Cells[3],
            FormatVariance(scheduled, actual),
            bold: actual.HasValue,
            color: actual.HasValue ? TextColor : MutedTextColor);
    }

    private static void AddPlannedFlightServices(Section section, ApprovedWorkOrderPrintDto source)
    {
        var plannedServices = source.Flight.PlannedServices;
        var assignedEmployees = source.Flight.AssignedEmployees;
        var table = CreateContentTable(section, 0.9, 11.3, 6.0);
        table.KeepTogether = plannedServices.Count <= 8 && assignedEmployees.Count <= 12;
        AddSectionHeaderRow(table, $"Planned Flight Services ({plannedServices.Count})");
        AddColumnHeaderRow(table, "#", "SERVICE", "CLASSIFICATION");

        if (plannedServices.Count == 0)
        {
            AddEmptyRow(table, "No planned flight services were recorded.");
        }
        else
        {
            for (var index = 0; index < plannedServices.Count; index++)
            {
                var service = plannedServices[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                AddCellText(row.Cells[0], (index + 1).ToString(CultureInfo.InvariantCulture), bold: true);
                AddCellText(row.Cells[1], service.Name, bold: true);
                AddCellText(
                    row.Cells[2],
                    service.IsAircraftPerLanding ? "Per Landing designation" : "Planned service");
            }
        }

        var roster = table.AddRow();
        roster.Cells[0].MergeRight = 2;
        roster.TopPadding = Unit.FromPoint(5);
        roster.BottomPadding = Unit.FromPoint(5);
        roster.Cells[0].Shading.Color = Color.Parse(AlternateRowColor);
        var rosterText = roster.Cells[0].AddParagraph();
        rosterText.Format.Font.Size = Unit.FromPoint(7.2);
        rosterText.AddFormattedText(
            $"ASSIGNED FLIGHT ROSTER ({assignedEmployees.Count}): ",
            TextFormat.Bold);
        rosterText.AddText(
            assignedEmployees.Count == 0
                ? "No planned roster recorded."
                : PdfSafeText(string.Join(
                    ", ",
                    assignedEmployees.Select(employee =>
                        FormatPerson(employee.FullName, employee.EmployeeId)))));

        if (plannedServices.Count <= 8 && assignedEmployees.Count <= 12)
            KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddPerformedServices(Section section, WorkOrderDetailDto workOrder)
    {
        var serviceLines = workOrder.ServiceLines;
        var table = CreateContentTable(section, 0.8, 5.4, 8.0, 4.0);
        AddSectionHeaderRow(table, $"Performed Services ({serviceLines.Count})");
        AddColumnHeaderRow(
            table,
            "#",
            "SERVICE",
            "WORK WINDOW (UTC)",
            "TIME");

        if (serviceLines.Count == 0)
        {
            AddEmptyRow(table, "No performed service lines were recorded.");
        }
        else
        {
            for (var index = 0; index < serviceLines.Count; index++)
            {
                var service = serviceLines[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                row.KeepWith = 1;
                AddCellText(row.Cells[0], (index + 1).ToString(CultureInfo.InvariantCulture), bold: true);
                AddServiceIdentity(row.Cells[1], service);
                AddCellText(row.Cells[2], FormatCompactWindow(service.FromUtc, service.ToUtc));
                AddCellText(
                    row.Cells[3],
                    FormatDuration(PositiveDuration(service.FromUtc, service.ToUtc)),
                    bold: true);

                AddServiceDetailRows(
                    table,
                    "PERFORMED BY",
                    service.PerformedBy.Count == 0
                        ? "Not recorded"
                        : string.Join(
                            ", ",
                            service.PerformedBy.Select(performer =>
                                FormatPerson(performer.FullName, performer.EmployeeId))),
                    alternate: index % 2 == 1);

                if (!string.IsNullOrWhiteSpace(service.Description))
                {
                    AddServiceDetailRows(
                        table,
                        "DETAILS",
                        service.Description!,
                        alternate: index % 2 == 1);
                }

                var attachmentCount = service.Attachments?.Count ?? 0;
                if (attachmentCount > 0)
                {
                    AddServiceDetailRows(
                        table,
                        "ATTACHMENTS",
                        $"{attachmentCount} attachment{(attachmentCount == 1 ? string.Empty : "s")}",
                        alternate: index % 2 == 1);
                }
            }
        }

        AddTableSpacing(section);
    }

    private static void AddServiceDetailRows(
        Table table,
        string label,
        string value,
        bool alternate)
    {
        var chunks = ChunkText(value);
        for (var index = 0; index < chunks.Count; index++)
        {
            var row = AddDataRow(table, alternate);
            row.Cells[0].MergeRight = table.Columns.Count - 1;
            row.TopPadding = Unit.FromPoint(index == 0 ? 3 : 1);
            row.BottomPadding = Unit.FromPoint(index == chunks.Count - 1 ? 4 : 1);

            var paragraph = row.Cells[0].AddParagraph();
            paragraph.Format.Font.Size = Unit.FromPoint(7.2);
            if (index == 0)
            {
                var labelText = paragraph.AddFormattedText($"{label}: ", TextFormat.Bold);
                labelText.Font.Color = Color.Parse(MutedTextColor);
            }

            paragraph.AddText(PdfSafeText(chunks[index]));
        }
    }

    private static IReadOnlyList<string> ChunkText(string? value, int maximumCharacters = 700)
    {
        var remaining = Clean(value);
        if (remaining.Length == 0)
            return ["Not recorded"];

        var chunks = new List<string>();
        while (remaining.Length > maximumCharacters)
        {
            var breakAt = remaining.LastIndexOfAny(
                ['\n', ' ', '\t', ',', ';'],
                maximumCharacters - 1,
                maximumCharacters);
            if (breakAt < maximumCharacters / 2)
                breakAt = maximumCharacters;

            chunks.Add(remaining[..breakAt].Trim());
            remaining = remaining[breakAt..].TrimStart();
        }

        if (remaining.Length > 0)
            chunks.Add(remaining);
        return chunks;
    }

    private static void AddServiceIdentity(Cell cell, WorkOrderServiceLineDto service)
    {
        var name = cell.AddParagraph(PdfSafeText(DisplayValue(service.ServiceName)));
        name.Format.Font.Size = Unit.FromPoint(7.2);
        name.Format.Font.Bold = true;

        if (!service.IsReturnToRamp)
            return;

        var returnToRamp = cell.AddParagraph("RETURN TO RAMP");
        returnToRamp.Format.Font.Size = Unit.FromPoint(5.8);
        returnToRamp.Format.Font.Bold = true;
        returnToRamp.Format.Font.Color = Color.Parse(BrandColor);
        returnToRamp.Format.SpaceBefore = Unit.FromPoint(2);
    }

    private static void AddReturnToRamp(Section section, WorkOrderDetailDto workOrder)
    {
        var activities = BuildReturnToRampActivities(workOrder);

        var table = CreateContentTable(section, 4.2, 4.2, 2.2, 7.6);
        AddSectionHeaderRow(table, "Return to Ramp");
        if (activities.Count == 0)
        {
            AddEmptyRow(table, "No return-to-ramp activity was recorded.");
        }
        else
        {
            AddColumnHeaderRow(table, "RETURNED AT", "RELEASED AT", "DURATION", "ACTIVITY");
            for (var index = 0; index < activities.Count; index++)
            {
                var activity = activities[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                AddCellText(row.Cells[0], FormatTimestamp(activity.FromUtc), bold: true);
                AddCellText(row.Cells[1], FormatTimestamp(activity.ToUtc), bold: true);
                AddCellText(
                    row.Cells[2],
                    FormatDuration(PositiveDuration(activity.FromUtc, activity.ToUtc)),
                    bold: true);
                AddCellText(row.Cells[3], activity.Label);
            }

            var totalRow = table.AddRow();
            totalRow.Cells[0].MergeRight = 1;
            totalRow.Cells[2].MergeRight = 1;
            totalRow.Shading.Color = Color.Parse(BrandSoftColor);
            totalRow.TopPadding = Unit.FromPoint(5);
            totalRow.BottomPadding = Unit.FromPoint(5);
            AddCellText(
                totalRow.Cells[0],
                "TOTAL RETURN-TO-RAMP TIME",
                bold: true,
                alignment: ParagraphAlignment.Right);
            AddCellText(
                totalRow.Cells[2],
                FormatDuration(CalculateReturnToRampDuration(workOrder)),
                bold: true);
        }

        AddTableSpacing(section);
    }

    private static IReadOnlyList<ReturnToRampActivity> BuildReturnToRampActivities(
        WorkOrderDetailDto workOrder) =>
        workOrder.ServiceLines
            .Where(service => service.IsReturnToRamp)
            .Select(service => new ReturnToRampActivity(
                service.FromUtc,
                service.ToUtc,
                service.ServiceName))
            .Concat(workOrder.Tasks
                .Select((task, index) => new { task, index })
                .Where(item => item.task.IsReturnToRamp)
                .Select(item => new ReturnToRampActivity(
                    item.task.FromUtc,
                    item.task.ToUtc,
                    $"CA-{item.index + 1:D2}")))
            .OrderBy(activity => activity.FromUtc)
            .ThenBy(activity => activity.ToUtc)
            .ToList();

    internal static TimeSpan CalculateReturnToRampDuration(WorkOrderDetailDto workOrder)
    {
        var activities = BuildReturnToRampActivities(workOrder);
        if (activities.Count == 0)
            return TimeSpan.Zero;

        var total = TimeSpan.Zero;
        var from = activities[0].FromUtc;
        var to = activities[0].ToUtc < from ? from : activities[0].ToUtc;
        foreach (var activity in activities.Skip(1))
        {
            var activityTo = activity.ToUtc < activity.FromUtc
                ? activity.FromUtc
                : activity.ToUtc;
            if (activity.FromUtc <= to)
            {
                if (activityTo > to)
                    to = activityTo;
                continue;
            }

            total += PositiveDuration(from, to);
            from = activity.FromUtc;
            to = activityTo;
        }

        return total + PositiveDuration(from, to);
    }

    internal static DateTimeOffset? ResolveHeaderTo(WorkOrderDetailDto workOrder)
    {
        var returnToRampEnd = workOrder.ServiceLines
            .Where(line => line.IsReturnToRamp)
            .Select(line => (DateTimeOffset?)line.ToUtc)
            .Concat(workOrder.Tasks
                .Where(task => task.IsReturnToRamp)
                .Select(task => (DateTimeOffset?)task.ToUtc))
            .Max();
        return returnToRampEnd ?? workOrder.ActualDepartureUtc;
    }

    private static void AddWorkOrderSummary(Section section, ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var workerWindows = BuildWorkerWindows(source);
        var mergedWindows = MergeWorkerWindows(workerWindows);
        var totalStaffTime = mergedWindows.Aggregate(
            TimeSpan.Zero,
            (total, window) => total + PositiveDuration(window.FromUtc, window.ToUtc));
        var major = workOrder.Tasks.Count(task =>
            task.TaskType.Equals("Major", StringComparison.OrdinalIgnoreCase));
        var minor = workOrder.Tasks.Count(task =>
            task.TaskType.Equals("Minor", StringComparison.OrdinalIgnoreCase));

        var table = CreateContentTable(section, 4.55, 4.55, 4.55, 4.55);
        table.KeepTogether = true;
        AddSectionHeaderRow(table, "Work Order Summary");
        var row = AddFactRow(table);
        AddFactCell(
            row.Cells[0],
            "Planned Services",
            source.Flight.PlannedServices.Count.ToString(CultureInfo.InvariantCulture));
        AddFactCell(
            row.Cells[1],
            "Performed Services",
            workOrder.ServiceLines.Count.ToString(CultureInfo.InvariantCulture));
        AddFactCell(
            row.Cells[2],
            "Corrective Actions",
            workOrder.Tasks.Count.ToString(CultureInfo.InvariantCulture),
            $"{major} Major / {minor} Minor");
        AddFactCell(
            row.Cells[3],
            "Total Staff Time",
            FormatDuration(totalStaffTime),
            $"{workerWindows.Select(window => window.StaffMemberId).Distinct().Count()} unique staff");

        KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddRemarks(Section section, WorkOrderDetailDto workOrder)
    {
        var table = CreateContentTable(section, ContentWidthCentimeters);
        AddSectionHeaderRow(table, "Remarks");
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(7);
        row.BottomPadding = Unit.FromPoint(8);
        AddCellText(
            row.Cells[0],
            string.IsNullOrWhiteSpace(workOrder.Remarks)
                ? "No remarks were recorded."
                : workOrder.Remarks,
            color: string.IsNullOrWhiteSpace(workOrder.Remarks) ? MutedTextColor : TextColor,
            fontSize: 8.2);
    }

    private static void AddCorrectiveActions(Section section, ApprovedWorkOrderPrintDto source)
    {
        var tasks = source.WorkOrder.Tasks;
        if (tasks.Count == 0)
        {
            var empty = CreateContentTable(section, ContentWidthCentimeters);
            AddSectionHeaderRow(empty, "Corrective Actions (0)");
            AddEmptyRow(empty, "No corrective actions were recorded.");
            AddTableSpacing(section);
            return;
        }

        for (var index = 0; index < tasks.Count; index++)
        {
            AddCorrectiveAction(section, source, tasks[index], index);
            AddTableSpacing(section, 5);
        }
    }

    private static void AddCorrectiveAction(
        Section section,
        ApprovedWorkOrderPrintDto source,
        WorkOrderTaskDto task,
        int index)
    {
        var table = CreateContentTable(section, 6.0, 3.5, 4.2, 4.5);
        var keepTogether = task.Employees.Count <= 12 &&
            task.Attachments.Count <= 4;

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.TopPadding = Unit.FromPoint(6);
        heading.BottomPadding = Unit.FromPoint(6);
        heading.Shading.Color = Color.Parse(HeaderFillColor);
        heading.Cells[0].MergeRight = 2;
        heading.Cells[0].Borders.Left.Color = Color.Parse(BrandColor);
        heading.Cells[0].Borders.Left.Width = Unit.FromPoint(4);
        var title = heading.Cells[0].AddParagraph($"CA-{index + 1:D2}  CORRECTIVE ACTION");
        title.Format.Font.Size = Unit.FromPoint(10.5);
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Color.Parse(TextColor);

        heading.Cells[3].Shading.Color = Color.Parse(BrandDarkColor);
        var badge = heading.Cells[3].AddParagraph(DisplayTaskType(task.TaskType).ToUpperInvariant());
        badge.Format.Alignment = ParagraphAlignment.Center;
        badge.Format.Font.Size = Unit.FromPoint(7.5);
        badge.Format.Font.Bold = true;
        badge.Format.Font.Color = Color.Parse(WhiteColor);

        var description = table.AddRow();
        description.Cells[0].MergeRight = 3;
        description.TopPadding = Unit.FromPoint(6);
        description.BottomPadding = Unit.FromPoint(7);
        var descriptionLabel = description.Cells[0].AddParagraph("ACTION RESULT / WORK PERFORMED");
        descriptionLabel.Format.Font.Size = Unit.FromPoint(6.3);
        descriptionLabel.Format.Font.Color = Color.Parse(MutedTextColor);
        var descriptionText = description.Cells[0].AddParagraph(
            PdfSafeText(string.IsNullOrWhiteSpace(task.Description)
                ? "No action result description was recorded."
                : task.Description));
        descriptionText.Format.Font.Size = Unit.FromPoint(8.1);
        descriptionText.Format.Font.Bold = !string.IsNullOrWhiteSpace(task.Description);
        descriptionText.Format.Font.Color = Color.Parse(
            string.IsNullOrWhiteSpace(task.Description) ? MutedTextColor : TextColor);
        descriptionText.Format.SpaceBefore = Unit.FromPoint(2);

        var facts = AddFactRow(table);
        AddFactCell(
            facts.Cells[0],
            "Work Period",
            FormatCompactWindow(task.FromUtc, task.ToUtc));
        AddFactCell(
            facts.Cells[1],
            "Duration",
            FormatDuration(PositiveDuration(task.FromUtc, task.ToUtc)));
        AddFactCell(
            facts.Cells[2],
            "Staff Assigned",
            task.Employees.Count.ToString(CultureInfo.InvariantCulture));
        AddFactCell(
            facts.Cells[3],
            "Return to Ramp",
            task.IsReturnToRamp ? "Yes" : "No");

        AddColumnHeaderRow(
            table,
            repeatOnContinuation: false,
            "STAFF MEMBER",
            "EMPLOYEE ID",
            "MANPOWER TYPE",
            "WORK WINDOW (UTC)");
        if (task.Employees.Count == 0)
        {
            AddEmptyRow(table, "No staff were assigned to this corrective action.");
        }
        else
        {
            var manpowerByStaffId = source.Staff
                .GroupBy(staff => staff.StaffMemberId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().ManpowerTypeName ?? string.Empty);
            for (var employeeIndex = 0; employeeIndex < task.Employees.Count; employeeIndex++)
            {
                var employee = task.Employees[employeeIndex];
                var row = AddDataRow(table, alternate: employeeIndex % 2 == 1);
                AddCellText(row.Cells[0], employee.FullName, bold: true);
                AddCellText(row.Cells[1], DisplayValue(employee.EmployeeId));
                AddCellText(
                    row.Cells[2],
                    DisplayValue(manpowerByStaffId.GetValueOrDefault(employee.StaffMemberId)));
                AddCellText(row.Cells[3], FormatCompactWindow(task.FromUtc, task.ToUtc));
            }
        }

        if (task.Attachments.Count > 0)
        {
            var attachments = table.AddRow();
            attachments.Cells[0].MergeRight = 3;
            attachments.TopPadding = Unit.FromPoint(4);
            attachments.BottomPadding = Unit.FromPoint(4);
            attachments.Cells[0].Shading.Color = Color.Parse(AlternateRowColor);
            var text = attachments.Cells[0].AddParagraph();
            text.Format.Font.Size = Unit.FromPoint(6.8);
            text.AddFormattedText(
                $"ATTACHMENTS ({task.Attachments.Count}): ",
                TextFormat.Bold);
            text.AddText(PdfSafeText(string.Join(
                ", ",
                task.Attachments.Select(attachment => attachment.OriginalFileName))));
        }

        if (keepTogether)
            KeepRowsTogether(table);
    }

    private static IReadOnlyList<ResourceLine> BuildResourceLines(
        WorkOrderDetailDto workOrder,
        ResourceKind kind)
    {
        var lines = new List<ResourceLine>();
        for (var taskIndex = 0; taskIndex < workOrder.Tasks.Count; taskIndex++)
        {
            var task = workOrder.Tasks[taskIndex];
            var action = $"CA-{taskIndex + 1:D2}";
            switch (kind)
            {
                case ResourceKind.Material:
                    lines.AddRange(task.Materials.Select(resource =>
                        new ResourceLine(action, resource.Name, resource.Quantity)));
                    break;
                case ResourceKind.Tool:
                    lines.AddRange(task.Tools.Select(resource =>
                        new ResourceLine(action, resource.Name, resource.Quantity)));
                    break;
                case ResourceKind.GeneralSupport:
                    lines.AddRange(task.GeneralSupports.Select(resource =>
                        new ResourceLine(action, resource.Name, resource.Quantity)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.");
            }
        }

        return lines;
    }

    private static void AddResourceRegister(
        Section section,
        string title,
        IReadOnlyList<ResourceLine> resources)
    {
        var table = CreateContentTable(section, 2.3, 12.2, 3.7);
        table.KeepTogether = resources.Count <= 12;
        AddSectionHeaderRow(table, $"{title} ({resources.Count})");
        AddColumnHeaderRow(table, "ACTION", "ITEM", "QUANTITY");

        if (resources.Count == 0)
        {
            AddEmptyRow(table, $"No {title.ToLowerInvariant()} were recorded.");
        }
        else
        {
            for (var index = 0; index < resources.Count; index++)
            {
                var resource = resources[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                AddCellText(row.Cells[0], resource.Action, bold: true);
                AddCellText(row.Cells[1], resource.Name);
                AddCellText(
                    row.Cells[2],
                    resource.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                    bold: true,
                    alignment: ParagraphAlignment.Center);
            }
        }

        if (resources.Count <= 12)
            KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddStaffUtilization(Section section, ApprovedWorkOrderPrintDto source)
    {
        var windows = MergeWorkerWindows(BuildWorkerWindows(source))
            .OrderBy(window => window.FromUtc)
            .ThenBy(window => window.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.StaffMemberId)
            .ToList();
        var table = CreateContentTable(section, 5.3, 2.7, 3.4, 4.6, 2.2);
        table.KeepTogether = windows.Count <= 12;
        AddSectionHeaderRow(table, $"Staff Utilization ({windows.Select(window => window.StaffMemberId).Distinct().Count()})");
        AddColumnHeaderRow(
            table,
            "STAFF MEMBER",
            "EMPLOYEE ID",
            "MANPOWER TYPE",
            "WORK WINDOW (UTC)",
            "TIME");

        if (windows.Count == 0)
        {
            AddEmptyRow(table, "No performed staff time was recorded.");
        }
        else
        {
            for (var index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                AddCellText(row.Cells[0], window.Name, bold: true);
                AddCellText(row.Cells[1], DisplayValue(window.EmployeeId));
                AddCellText(row.Cells[2], DisplayValue(window.ManpowerTypeName));
                AddCellText(row.Cells[3], FormatCompactWindow(window.FromUtc, window.ToUtc));
                AddCellText(
                    row.Cells[4],
                    FormatDuration(PositiveDuration(window.FromUtc, window.ToUtc)),
                    bold: true,
                    alignment: ParagraphAlignment.Center);
            }

            var total = windows.Aggregate(
                TimeSpan.Zero,
                (sum, window) => sum + PositiveDuration(window.FromUtc, window.ToUtc));
            var totalRow = table.AddRow();
            totalRow.Cells[0].MergeRight = 3;
            totalRow.Shading.Color = Color.Parse(BrandSoftColor);
            totalRow.TopPadding = Unit.FromPoint(5);
            totalRow.BottomPadding = Unit.FromPoint(5);
            AddCellText(
                totalRow.Cells[0],
                "TOTAL STAFF TIME",
                bold: true,
                alignment: ParagraphAlignment.Right);
            AddCellText(
                totalRow.Cells[4],
                FormatDuration(total),
                bold: true,
                alignment: ParagraphAlignment.Center);
        }

        if (windows.Count <= 12)
            KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static IReadOnlyList<WorkerWindow> BuildWorkerWindows(ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var manpowerByStaffId = source.Staff
            .GroupBy(staff => staff.StaffMemberId)
            .ToDictionary(
                group => group.Key,
                group => group.First().ManpowerTypeName ?? string.Empty);

        return workOrder.ServiceLines
            .SelectMany(line => line.PerformedBy.Select(performer => new WorkerWindow(
                performer.StaffMemberId,
                performer.FullName,
                manpowerByStaffId.GetValueOrDefault(performer.StaffMemberId, string.Empty),
                line.FromUtc,
                line.ToUtc,
                performer.EmployeeId)))
            .Concat(workOrder.Tasks.SelectMany(task => task.Employees.Select(employee =>
                new WorkerWindow(
                    employee.StaffMemberId,
                    employee.FullName,
                    manpowerByStaffId.GetValueOrDefault(employee.StaffMemberId, string.Empty),
                    task.FromUtc,
                    task.ToUtc,
                    employee.EmployeeId))))
            .Where(window => !string.IsNullOrWhiteSpace(window.Name))
            .ToList();
    }

    internal static IReadOnlyList<WorkerWindow> MergeWorkerWindows(IReadOnlyList<WorkerWindow> source)
    {
        var merged = new List<WorkerWindow>();
        foreach (var group in source.GroupBy(window => window.StaffMemberId))
        {
            var ordered = group
                .OrderBy(window => window.FromUtc)
                .ThenBy(window => window.ToUtc)
                .ToList();
            if (ordered.Count == 0)
                continue;

            var from = ordered[0].FromUtc;
            var to = ordered[0].ToUtc;
            var identity = ordered[0];
            foreach (var window in ordered.Skip(1))
            {
                if (window.FromUtc <= to)
                {
                    if (window.ToUtc > to)
                        to = window.ToUtc;
                    continue;
                }

                merged.Add(identity with { FromUtc = from, ToUtc = to });
                from = window.FromUtc;
                to = window.ToUtc;
            }

            merged.Add(identity with { FromUtc = from, ToUtc = to });
        }

        return merged;
    }

    private static void AddAttachmentRegister(Section section, WorkOrderDetailDto workOrder)
    {
        var attachments = new List<AttachmentLine>();
        for (var serviceIndex = 0; serviceIndex < workOrder.ServiceLines.Count; serviceIndex++)
        {
            var service = workOrder.ServiceLines[serviceIndex];
            attachments.AddRange((service.Attachments ?? []).Select(attachment => new AttachmentLine(
                $"Service {serviceIndex + 1}",
                attachment.Kind,
                attachment.OriginalFileName,
                attachment.ContentType,
                attachment.Size)));
        }

        for (var taskIndex = 0; taskIndex < workOrder.Tasks.Count; taskIndex++)
        {
            attachments.AddRange(workOrder.Tasks[taskIndex].Attachments.Select(attachment => new AttachmentLine(
                $"CA-{taskIndex + 1:D2}",
                attachment.Kind,
                attachment.OriginalFileName,
                attachment.ContentType,
                attachment.Size)));
        }

        var table = CreateContentTable(section, 2.4, 2.3, 7.4, 3.8, 2.3);
        table.KeepTogether = attachments.Count <= 12;
        AddSectionHeaderRow(table, $"Attachment Register ({attachments.Count})");
        AddColumnHeaderRow(table, "SOURCE", "KIND", "FILE", "CONTENT TYPE", "SIZE");
        if (attachments.Count == 0)
        {
            AddEmptyRow(table, "No work-order attachments were recorded.");
        }
        else
        {
            for (var index = 0; index < attachments.Count; index++)
            {
                var attachment = attachments[index];
                var row = AddDataRow(table, alternate: index % 2 == 1);
                AddCellText(row.Cells[0], attachment.Source, bold: true);
                AddCellText(row.Cells[1], attachment.Kind);
                AddCellText(row.Cells[2], attachment.FileName);
                AddCellText(row.Cells[3], attachment.ContentType);
                AddCellText(
                    row.Cells[4],
                    FormatFileSize(attachment.Size),
                    alignment: ParagraphAlignment.Right);
            }
        }

        if (attachments.Count <= 12)
            KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddCustomerAcceptance(Section section, ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var table = CreateContentTable(section, 7.0, 5.6, 5.6);
        table.KeepTogether = true;
        AddSectionHeaderRow(table, "Approval and Customer Acceptance");

        var statementRow = table.AddRow();
        statementRow.Cells[0].MergeRight = 2;
        statementRow.TopPadding = Unit.FromPoint(6);
        statementRow.BottomPadding = Unit.FromPoint(6);
        AddCellText(
            statementRow.Cells[0],
            "The customer signature confirms that the work described in this work order was completed and accepted.",
            fontSize: 7.6);

        var signatureRow = table.AddRow();
        signatureRow.TopPadding = Unit.FromPoint(6);
        signatureRow.BottomPadding = Unit.FromPoint(6);
        AddSignatureCell(signatureRow.Cells[0], source.CustomerSignatureContent);
        AddFactCell(
            signatureRow.Cells[1],
            "Customer Signed At",
            FormatTimestamp(workOrder.CustomerSignature?.SignedAtUtc));
        AddFactCell(
            signatureRow.Cells[2],
            "Approval Number",
            DisplayValue(workOrder.ApprovalNumber));

        var approvalRow = AddFactRow(table);
        AddFactCell(
            approvalRow.Cells[0],
            "Work Order Owner",
            DisplayValue(workOrder.OwnerName));
        AddFactCell(
            approvalRow.Cells[1],
            "Approved At",
            FormatTimestamp(workOrder.ApprovedAtUtc));
        AddFactCell(
            approvalRow.Cells[2],
            "Record ID",
            workOrder.Id.ToString("D", CultureInfo.InvariantCulture));

        KeepRowsTogether(table);
        AddTableSpacing(section);
    }

    private static void AddSignatureCell(Cell cell, byte[]? content)
    {
        var label = cell.AddParagraph("CUSTOMER SIGNATURE");
        label.Format.Font.Size = Unit.FromPoint(6.3);
        label.Format.Font.Color = Color.Parse(MutedTextColor);

        var imageData = TryGetImageDataUri(content);
        if (imageData is null)
        {
            var missing = cell.AddParagraph("No signature image available.");
            missing.Format.Font.Size = Unit.FromPoint(7.2);
            missing.Format.Font.Color = Color.Parse(MutedTextColor);
            missing.Format.SpaceBefore = Unit.FromPoint(7);
            return;
        }

        var image = cell.AddImage(imageData);
        image.LockAspectRatio = true;
        image.Height = Unit.FromCentimeter(1.15);
    }

    private static void AddDocumentControl(Section section, WorkOrderDetailDto workOrder)
    {
        var table = CreateContentTable(section, 4.55, 4.55, 4.55, 4.55);
        table.KeepTogether = true;
        AddSectionHeaderRow(table, "Document Control");
        var row = AddFactRow(table);
        AddFactCell(row.Cells[0], "Template", "WO-FLIGHT-02");
        AddFactCell(row.Cells[1], "Revision", "1");
        AddFactCell(row.Cells[2], "Time Zone", "UTC");
        AddFactCell(
            row.Cells[3],
            "Approved Record",
            DisplayValue(workOrder.ApprovalNumber));
        KeepRowsTogether(table);
    }

    private static Table CreateContentTable(Section section, params double[] widths)
    {
        var table = ConfigureTable(section.AddTable(), widths);
        table.Borders.Color = Color.Parse(BorderColor);
        table.Borders.Width = Unit.FromPoint(0.45);
        table.Format.Font.Name = FontFamily;
        table.Format.Font.Size = Unit.FromPoint(7.2);
        return table;
    }

    private static Table ConfigureTable(Table table, params double[] widths)
    {
        var total = widths.Sum();
        if (Math.Abs(total - ContentWidthCentimeters) > 0.001)
        {
            throw new InvalidOperationException(
                $"PDF table width must be {ContentWidthCentimeters:0.0} cm, but was {total:0.###} cm.");
        }

        table.Rows.LeftIndent = Unit.Zero;
        table.LeftPadding = Unit.FromPoint(5);
        table.RightPadding = Unit.FromPoint(5);
        foreach (var width in widths)
            table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }

    private static void KeepRowsTogether(Table table)
    {
        table.KeepTogether = true;
        for (var index = 0; index < table.Rows.Count - 1; index++)
            table.Rows[index].KeepWith = 1;
    }

    private static void AddSectionHeaderRow(Table table, string title)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.TopPadding = Unit.FromPoint(5.5);
        row.BottomPadding = Unit.FromPoint(5.5);
        row.Shading.Color = Color.Parse(HeaderFillColor);
        row.Cells[0].MergeRight = table.Columns.Count - 1;
        row.Cells[0].Borders.Left.Color = Color.Parse(BrandColor);
        row.Cells[0].Borders.Left.Width = Unit.FromPoint(4);

        var paragraph = row.Cells[0].AddParagraph(PdfSafeText(title));
        paragraph.Format.Font.Size = Unit.FromPoint(10);
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Color = Color.Parse(TextColor);
    }

    private static void AddColumnHeaderRow(
        Table table,
        params string[] headings) =>
        AddColumnHeaderRow(table, repeatOnContinuation: true, headings);

    private static void AddColumnHeaderRow(
        Table table,
        bool repeatOnContinuation,
        params string[] headings)
    {
        if (headings.Length != table.Columns.Count)
            throw new ArgumentException("A heading is required for every PDF table column.", nameof(headings));

        var row = table.AddRow();
        row.HeadingFormat = repeatOnContinuation;
        row.TopPadding = Unit.FromPoint(4);
        row.BottomPadding = Unit.FromPoint(4);
        row.Shading.Color = Color.Parse(ColumnHeaderColor);
        row.Format.Font.Size = Unit.FromPoint(6.6);
        row.Format.Font.Bold = true;
        row.Format.Font.Color = Color.Parse(TextColor);
        for (var index = 0; index < headings.Length; index++)
            row.Cells[index].AddParagraph(PdfSafeText(headings[index]));
    }

    private static Row AddFactRow(Table table)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(6);
        row.BottomPadding = Unit.FromPoint(6);
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    private static void AddFactCell(
        Cell cell,
        string label,
        string? value,
        string? secondary = null)
    {
        var labelParagraph = cell.AddParagraph(PdfSafeText(label.ToUpperInvariant()));
        labelParagraph.Format.Font.Size = Unit.FromPoint(6.3);
        labelParagraph.Format.Font.Color = Color.Parse(MutedTextColor);

        var valueParagraph = cell.AddParagraph(PdfSafeText(DisplayValue(value)));
        valueParagraph.Format.Font.Size = Unit.FromPoint(8.5);
        valueParagraph.Format.Font.Bold = true;
        valueParagraph.Format.Font.Color = Color.Parse(TextColor);
        valueParagraph.Format.SpaceBefore = Unit.FromPoint(1);

        if (string.IsNullOrWhiteSpace(secondary))
            return;

        var secondaryParagraph = cell.AddParagraph(PdfSafeText(secondary));
        secondaryParagraph.Format.Font.Size = Unit.FromPoint(6.2);
        secondaryParagraph.Format.Font.Color = Color.Parse(MutedTextColor);
        secondaryParagraph.Format.SpaceBefore = Unit.FromPoint(1);
    }

    private static Row AddDataRow(Table table, bool alternate = false)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(4);
        row.BottomPadding = Unit.FromPoint(4);
        row.VerticalAlignment = VerticalAlignment.Center;
        if (alternate)
            row.Shading.Color = Color.Parse(AlternateRowColor);
        return row;
    }

    private static void AddEmptyRow(Table table, string message)
    {
        var row = table.AddRow();
        row.Cells[0].MergeRight = table.Columns.Count - 1;
        row.TopPadding = Unit.FromPoint(7);
        row.BottomPadding = Unit.FromPoint(7);
        AddCellText(row.Cells[0], message, color: MutedTextColor, fontSize: 7.6);
    }

    private static void AddCellText(
        Cell cell,
        string? value,
        bool bold = false,
        string? color = null,
        double fontSize = 7.2,
        ParagraphAlignment? alignment = null)
    {
        var paragraph = cell.AddParagraph(PdfSafeText(DisplayValue(value)));
        paragraph.Format.Font.Size = Unit.FromPoint(fontSize);
        paragraph.Format.Font.Bold = bold;
        paragraph.Format.Font.Color = Color.Parse(color ?? TextColor);
        if (alignment.HasValue)
            paragraph.Format.Alignment = alignment.Value;
    }

    private static void AddTableSpacing(Section section, double points = 7)
    {
        var spacer = section.AddParagraph();
        spacer.Format.Font.Size = Unit.FromPoint(1);
        spacer.Format.SpaceAfter = Unit.FromPoint(points);
    }

    private static string DisplayTaskType(string? taskType)
    {
        var value = Clean(taskType);
        return value.Length == 0 || value.Equals("Task", StringComparison.OrdinalIgnoreCase)
            ? "Action"
            : value;
    }

    internal static string DisplayFlightNumber(WorkOrderDetailDto workOrder, string? number)
    {
        var flightNumber = Clean(number);
        if (flightNumber.Length == 0)
            return "Not recorded";

        var carrierCode = Clean(workOrder.CustomerIataCode).ToUpperInvariant();
        if (carrierCode.Length == 0)
            return flightNumber;

        if (flightNumber.StartsWith(carrierCode, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = flightNumber[carrierCode.Length..]
                .TrimStart(' ', '-', '\u2013', '\u2014');
            return suffix.Length == 0 ? carrierCode : $"{carrierCode}-{suffix}";
        }

        return $"{carrierCode}-{flightNumber}";
    }

    private static string JoinCodeAndName(string? code, string? name)
    {
        var cleanCode = Clean(code).ToUpperInvariant();
        var cleanName = Clean(name);
        if (cleanCode.Length == 0)
            return DisplayValue(cleanName);
        return cleanName.Length == 0 ? cleanCode : $"{cleanCode} - {cleanName}";
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value.HasValue ? FormatTimestamp(value.Value) : "Not recorded";

    private static string FormatCompactWindow(DateTimeOffset from, DateTimeOffset to)
    {
        var utcFrom = from.UtcDateTime;
        var utcTo = to.UtcDateTime;
        return utcFrom.Date == utcTo.Date
            ? $"{utcFrom:dd MMM yyyy HH:mm} - {utcTo:HH:mm}"
            : $"{utcFrom:dd MMM HH:mm} - {utcTo:dd MMM HH:mm}";
    }

    private static string FormatVariance(DateTimeOffset scheduled, DateTimeOffset? actual)
    {
        if (!actual.HasValue)
            return "Not recorded";

        var minutes = (int)Math.Round((actual.Value - scheduled).TotalMinutes);
        return minutes switch
        {
            0 => "On time",
            > 0 => $"{minutes} min late",
            _ => $"{Math.Abs(minutes)} min early"
        };
    }

    private static TimeSpan PositiveDuration(DateTimeOffset from, DateTimeOffset to) =>
        to >= from ? to - from : TimeSpan.Zero;

    private static string FormatDuration(TimeSpan value)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(value.TotalMinutes));
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}h {totalMinutes % 60:D2}m"
            : $"{totalMinutes}m";
    }

    private static string FormatFileSize(long size)
    {
        if (size < 1_024)
            return $"{Math.Max(0, size)} B";
        if (size < 1_048_576)
            return $"{size / 1_024d:0.#} KB";
        return $"{size / 1_048_576d:0.#} MB";
    }

    private static string FormatPerson(string? name, string? employeeId)
    {
        var cleanName = DisplayValue(name);
        var cleanEmployeeId = Clean(employeeId);
        return cleanEmployeeId.Length == 0
            ? cleanName
            : $"{cleanName} ({cleanEmployeeId})";
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(
            ' ',
            values
                .Select(Clean)
                .Where(value => value.Length > 0));

    private static string DisplayValue(string? value)
    {
        var cleaned = Clean(value);
        return cleaned.Length == 0 ? "Not recorded" : cleaned;
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(
            value.Trim()
                .Where(character => !char.IsControl(character) || character is '\n' or '\t')
                .ToArray());
    }

    private static string PdfSafeText(string? value)
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
        using var stream = PdfDocumentAssets.OpenEmbeddedResource(LogoResource);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return "base64:" + Convert.ToBase64String(memory.ToArray());
    }

    private static string? TryGetImageDataUri(byte[]? content)
    {
        if (content is not { Length: > 0 })
            return null;

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var image = XImage.FromStream(stream);
            return "base64:" + Convert.ToBase64String(content);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string BuildFileName(string? approvalNumber)
    {
        var safeNumber = string.IsNullOrWhiteSpace(approvalNumber)
            ? "approved"
            : string.Concat(approvalNumber.Trim().Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        return $"work-order-{safeNumber}.pdf";
    }

    internal sealed record HeaderDetail(string Label, string Value);

    internal sealed record WorkerWindow(
        Guid StaffMemberId,
        string Name,
        string ManpowerTypeName,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string EmployeeId = "");

    private sealed record ReturnToRampActivity(
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string Label);

    private sealed record ResourceLine(string Action, string Name, decimal Quantity);

    private sealed record AttachmentLine(
        string Source,
        string Kind,
        string FileName,
        string ContentType,
        long Size);

    private enum ResourceKind
    {
        Material,
        Tool,
        GeneralSupport
    }
}
