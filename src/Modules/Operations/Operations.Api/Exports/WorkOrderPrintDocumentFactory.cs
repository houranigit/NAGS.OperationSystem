using System.Globalization;
using Operations.Application.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Operations.Api.Exports;

internal sealed record WorkOrderPrintFile(byte[] Content, string FileName);

/// <summary>
/// Fills the approved work-order data onto the historic NAGS form. The supplied Illustrator PDF is
/// kept as a vector background so the printed document preserves the original geometry and branding.
/// Fields that do not exist in the Operations model intentionally remain blank.
/// </summary>
internal static class WorkOrderPrintDocumentFactory
{
    private const string TemplateResource = "Operations.Api.Assets.WorkOrderTemplate.pdf";
    private const string LogoResource = "Operations.Api.Assets.NagsLogo.png";
    private const string FontFamily = PdfDocumentAssets.FontFamily;

    private static readonly XColor TextColor = XColor.FromArgb(0x23, 0x1F, 0x20);
    private static readonly XColor AccentRed = XColor.FromArgb(0xED, 0x1C, 0x24);
    private static readonly XColor HeaderFill = XColor.FromArgb(0xE8, 0xE6, 0xE6);

    public static WorkOrderPrintFile Create(ApprovedWorkOrderPrintDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfDocumentAssets.EnsureFontResolver();

        using var templateStream = PdfDocumentAssets.OpenEmbeddedResource(TemplateResource);
        using var template = XPdfForm.FromStream(templateStream);
        using var document = new PdfDocument();

        var workOrder = source.WorkOrder;
        document.Info.Title = $"Work Order {workOrder.ApprovalNumber}";
        document.Info.Subject = "Approved flight work order";
        document.Info.Author = "National Aviation Ground Support";

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(template.PointWidth);
        page.Height = XUnit.FromPoint(template.PointHeight);

        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawImage(template, 0, 0, template.PointWidth, template.PointHeight);
            DrawApprovedWorkOrder(graphics, source);
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return new WorkOrderPrintFile(output.ToArray(), BuildFileName(workOrder.ApprovalNumber));
    }

    private static void DrawApprovedWorkOrder(XGraphics graphics, ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var regular = new XFont(FontFamily, 7.2, XFontStyleEx.Regular);

        DrawSystemLogo(graphics);

        // The historical blank contains a literal red "001". Cover only the sample value, leaving
        // the vector label and cell rules intact, then draw the actual approval number.
        graphics.DrawRectangle(XBrushes.White, 515.5, 24.5, 45.5, 23);
        DrawSingleLine(graphics, workOrder.ApprovalNumber, new XRect(488, 24, 73, 24), 11.2,
            XFontStyleEx.Bold, new XSolidBrush(AccentRed), XStringFormats.CenterRight, minimumSize: 7);

        DrawSingleLine(graphics, source.ContractNumber, new XRect(489.5, 49.5, 70.5, 16), 7,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);

        var aircraftType = JoinNonEmpty(source.AircraftManufacturer, workOrder.AircraftTypeModel);
        DrawSingleLine(graphics, workOrder.CustomerName, new XRect(34, 90, 179, 19), 7.4,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, workOrder.AircraftTailNumber, new XRect(215, 90, 58, 19), 7.2,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, aircraftType, new XRect(275, 90, 75, 19), 7,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, DisplayFlightNumber(workOrder), new XRect(352, 90, 84, 19), 7.2,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawHeaderMovementTimes(graphics, workOrder);

        DrawRemarks(graphics, workOrder, regular);
        DrawRequestedServices(graphics, workOrder);
        DrawFlightTypeCheck(graphics, workOrder.OperationTypeName);
        DrawReturnToRamp(graphics, workOrder);
        DrawFlightTimes(graphics, workOrder);
        DrawCorrectiveAction(graphics, workOrder, regular);
        DrawCorrectiveActionHeader(graphics, workOrder);

        DrawStaff(graphics, source);
        DrawMaterials(graphics, workOrder);
        DrawCustomerAcceptance(graphics, source);
    }

    private static void DrawSystemLogo(XGraphics graphics)
    {
        graphics.DrawRectangle(XBrushes.White, 32.5, 21.7, 118.5, 49.8);
        using var logoStream = PdfDocumentAssets.OpenEmbeddedResource(LogoResource);
        using var logo = XImage.FromStream(logoStream);
        var bounds = new XRect(68.5, 22.8, 46, 48.5);
        var scale = Math.Min(bounds.Width / logo.PointWidth, bounds.Height / logo.PointHeight);
        var width = logo.PointWidth * scale;
        var height = logo.PointHeight * scale;
        graphics.DrawImage(
            logo,
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2,
            width,
            height);
    }

    private static void DrawHeaderMovementTimes(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        DrawSingleLine(graphics, FormatCompactDateTime(workOrder.ActualArrivalUtc),
            new XRect(437.5, 90, 63, 19), 5.5, XFontStyleEx.Bold,
            XBrushes.Black, XStringFormats.Center, minimumSize: 4.5);
        DrawSingleLine(graphics, FormatCompactDateTime(ResolveHeaderTo(workOrder)),
            new XRect(501.5, 90, 62.5, 19), 5.5, XFontStyleEx.Bold,
            XBrushes.Black, XStringFormats.Center, minimumSize: 4.5);
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

    private static void DrawRemarks(XGraphics graphics, WorkOrderDetailDto workOrder, XFont font)
    {
        graphics.DrawRectangle(XBrushes.White, 34.5, 112.0, 108.0, 27.5);
        DrawSingleLine(graphics, "Remarks:", new XRect(38, 112, 100, 27), 8,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);

        if (!string.IsNullOrWhiteSpace(workOrder.Remarks))
        {
            DrawOnRuledLines(graphics, [workOrder.Remarks],
                new XRect(35, 143.7, 272, 214.5), font, 19.42, 11);
        }
    }

    private static void DrawRequestedServices(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        var rows = BuildRequestedServiceRows(workOrder);
        var rowBounds = new[]
        {
            (Top: 130.0, Bottom: 148.2),
            (Top: 148.5, Bottom: 167.0),
            (Top: 167.3, Bottom: 185.7),
            (Top: 186.0, Bottom: 204.3),
            (Top: 204.6, Bottom: 221.8)
        };

        for (var index = 0; index < rowBounds.Length; index++)
        {
            var bounds = rowBounds[index];
            graphics.DrawRectangle(XBrushes.White, 313.4, bounds.Top + 0.7, 120.7,
                bounds.Bottom - bounds.Top - 1.4);
            if (index < rows.Count)
            {
                DrawSingleLine(graphics, rows[index],
                    new XRect(317, bounds.Top, 113.5, bounds.Bottom - bounds.Top),
                    7.0, XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft,
                    minimumSize: 5.2);
            }
        }
    }

    internal static IReadOnlyList<string> BuildRequestedServiceRows(WorkOrderDetailDto workOrder)
    {
        var serviceNames = workOrder.ServiceLines
            .Select(line => Clean(line.ServiceName))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (serviceNames.Count <= 5)
            return serviceNames;

        return serviceNames.Take(4)
            .Append($"More {serviceNames.Count - 4} Services")
            .ToList();
    }

    private static void DrawFlightTypeCheck(XGraphics graphics, string operationTypeName)
    {
        var operation = Normalize(operationTypeName);
        var y = operation switch
        {
            var value when value.Contains("schedule", StringComparison.Ordinal) => 133.2,
            var value when value.Contains("umrah", StringComparison.Ordinal) => 151.8,
            var value when value.Contains("adhoc", StringComparison.Ordinal) => 170.4,
            var value when value.Contains("hajj", StringComparison.Ordinal) => 189.0,
            var value when value.Contains("extra", StringComparison.Ordinal) => 207.3,
            _ => (double?)null
        };

        if (y.HasValue)
            DrawCheck(graphics, 544.2, y.Value, 12.1);
    }

    private static void DrawReturnToRamp(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        graphics.DrawRectangle(new XSolidBrush(HeaderFill), 313.5, 225.0, 248.5, 17.2);
        DrawSingleLine(graphics, "Return to Ramp:", new XRect(318, 225, 120, 17.2), 7.8,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);

        var windows = workOrder.ServiceLines
            .Where(line => line.IsReturnToRamp)
            .Select(line => (line.FromUtc, line.ToUtc))
            .Concat(workOrder.Tasks.Where(task => task.IsReturnToRamp).Select(task => (task.FromUtc, task.ToUtc)))
            .OrderBy(window => window.FromUtc)
            .ToList();

        if (windows.Count == 0)
            return;

        var from = windows.Min(window => window.FromUtc);
        var to = windows.Max(window => window.ToUtc);
        DrawSingleLine(graphics, FormatDateTime(from), new XRect(441, 244, 120, 16), 6.6,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);
        DrawSingleLine(graphics, FormatDateTime(to), new XRect(441, 262, 120, 16), 6.6,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);
    }

    private static void DrawFlightTimes(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        DrawTimeRow(graphics, workOrder.ScheduledArrivalUtc, 282.0, 300.2);
        DrawTimeRow(graphics, workOrder.ScheduledDepartureUtc, 300.7, 318.3);
        DrawTimeRow(graphics, workOrder.ActualArrivalUtc, 320.6, 339.0);
        DrawTimeRow(graphics, workOrder.ActualDepartureUtc, 339.5, 358.5);
    }

    private static void DrawTimeRow(XGraphics graphics, DateTimeOffset? value, double top, double bottom)
    {
        // The template contains a partial 201x date in each value cell. Patch the interior only so
        // its borders and the static "Date" label remain vector sharp.
        graphics.DrawRectangle(XBrushes.White, 473.0, top + 1.2, 88.4, Math.Max(1, bottom - top - 2.4));
        if (value is not { } timestamp)
            return;

        DrawSingleLine(graphics, timestamp.UtcDateTime.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture),
            new XRect(349, top, 90, bottom - top), 7.0, XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, timestamp.UtcDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            new XRect(473, top, 88, bottom - top), 6.8, XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
    }

    private static void DrawCorrectiveAction(XGraphics graphics, WorkOrderDetailDto workOrder, XFont font)
    {
        DrawOnRuledLines(graphics, BuildCorrectiveActionRows(workOrder),
            new XRect(35, 398.2, 272, 289.5), font, 19.42, 15);
    }

    private static void DrawCorrectiveActionHeader(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        var pen = new XPen(XColors.Black, 0.65);
        graphics.DrawRectangle(new XSolidBrush(HeaderFill), 125.3, 362.0, 183.5, 34.8);
        graphics.DrawLine(pen, 125.0, 379.5, 309.0, 379.5);

        DrawSingleLine(graphics, "Major", new XRect(132, 362, 37, 17.5), 7.8,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);
        DrawSingleLine(graphics, "Minor", new XRect(132, 379.5, 37, 17.5), 7.8,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);
        DrawCheckbox(graphics, 171.0, 364.3, 12.1,
            workOrder.Tasks.Any(task => task.TaskType.Equals("Major", StringComparison.OrdinalIgnoreCase)));
        DrawCheckbox(graphics, 170.7, 382.0, 12.1,
            workOrder.Tasks.Any(task => task.TaskType.Equals("Minor", StringComparison.OrdinalIgnoreCase)));
    }

    internal static IReadOnlyList<string> BuildCorrectiveActionRows(WorkOrderDetailDto workOrder) =>
        workOrder.Tasks
            .OrderBy(task => task.FromUtc)
            .ThenBy(task => task.Id)
            .Select(task =>
            {
                var type = task.TaskType.Equals("Major", StringComparison.OrdinalIgnoreCase)
                    ? "Major"
                    : task.TaskType.Equals("Minor", StringComparison.OrdinalIgnoreCase)
                        ? "Minor"
                        : Clean(task.TaskType);
                var label = type.Length == 0 || type.Equals("Task", StringComparison.OrdinalIgnoreCase)
                    ? "Task"
                    : $"{type} Task";
                var staffNames = task.Employees
                    .Select(employee => Clean(employee.FullName))
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (staffNames.Count > 0)
                    label += $" By {string.Join(", ", staffNames)}";
                if (!string.IsNullOrWhiteSpace(task.Description))
                    label += $", {Clean(task.Description)}";
                return label;
            })
            .ToList();

    private static void DrawStaff(XGraphics graphics, ApprovedWorkOrderPrintDto source)
    {
        var workOrder = source.WorkOrder;
        var manpowerByStaffId = source.Staff
            .GroupBy(staff => staff.StaffMemberId)
            .ToDictionary(group => group.Key, group => group.First().ManpowerTypeName ?? string.Empty);
        var windows = MergeWorkerWindows(workOrder.ServiceLines
            .SelectMany(line => line.PerformedBy.Select(performer => new WorkerWindow(
                performer.StaffMemberId,
                performer.FullName,
                manpowerByStaffId.GetValueOrDefault(performer.StaffMemberId, string.Empty),
                line.FromUtc,
                line.ToUtc)))
            .Concat(workOrder.Tasks.SelectMany(task => task.Employees.Select(employee =>
                new WorkerWindow(
                    employee.StaffMemberId,
                    employee.FullName,
                    manpowerByStaffId.GetValueOrDefault(employee.StaffMemberId, string.Empty),
                    task.FromUtc,
                    task.ToUtc))))
            .Where(window => !string.IsNullOrWhiteSpace(window.Name))
            .ToList())
            .OrderBy(window => window.FromUtc)
            .ThenBy(window => window.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staffRows = windows
            .Select(window => new StaffRow(
                window.Name,
                window.ManpowerTypeName,
                window.FromUtc,
                window.ToUtc))
            .ToList();

        var rowSlots = Math.Max(6, staffRows.Count);
        var rowHeight = 270.0 / rowSlots;
        DrawStaffTableGrid(graphics, rowSlots, rowHeight);
        for (var index = 0; index < staffRows.Count; index++)
        {
            var row = staffRows[index];
            var top = 397.0 + index * rowHeight;
            if (rowHeight >= 20)
            {
                var nameFontSize = Math.Clamp(rowHeight * 0.19, 4.0, 6.3);
                var typeFontSize = Math.Clamp(rowHeight * 0.16, 3.5, 5.5);
                DrawSingleLine(graphics, row.Name, new XRect(317, top + rowHeight * 0.08, 96, rowHeight * 0.42), nameFontSize,
                    XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft, minimumSize: 3.8);
                DrawSingleLine(graphics,
                    string.IsNullOrWhiteSpace(row.ManpowerTypeName) ? "Manpower: Not available" : row.ManpowerTypeName,
                    new XRect(317, top + rowHeight * 0.50, 96, rowHeight * 0.38), typeFontSize, XFontStyleEx.Regular,
                    XBrushes.Black, XStringFormats.CenterLeft, minimumSize: 3.3);
            }
            else
            {
                var identity = string.IsNullOrWhiteSpace(row.ManpowerTypeName)
                    ? row.Name
                    : $"{row.Name} ({row.ManpowerTypeName})";
                var compactFontSize = Math.Clamp(rowHeight * 0.26, 3.8, 5.8);
                DrawSingleLine(graphics, identity, new XRect(317, top, 96, rowHeight), compactFontSize,
                    XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft, minimumSize: 3.5);
            }

            var valueFontSize = Math.Clamp(rowHeight * 0.16, 4.0, 6.2);
            DrawSingleLine(graphics, row.FromUtc.UtcDateTime.ToString("dd/MM/yy", CultureInfo.InvariantCulture),
                new XRect(418, top, 48, rowHeight), valueFontSize, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center, minimumSize: 3.8);
            DrawSingleLine(graphics, row.FromUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                new XRect(468, top, 48, rowHeight), valueFontSize, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center, minimumSize: 3.8);
            DrawSingleLine(graphics, row.ToUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                new XRect(518, top, 44, rowHeight), valueFontSize, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center, minimumSize: 3.8);
        }

        var total = windows.Aggregate(TimeSpan.Zero, (sum, window) =>
            sum + (window.ToUtc >= window.FromUtc ? window.ToUtc - window.FromUtc : TimeSpan.Zero));
        DrawSingleLine(graphics, total > TimeSpan.Zero ? FormatDuration(total) : "0m",
            new XRect(444, 667.0, 118, 21.0), 7,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
    }

    private static void DrawStaffTableGrid(XGraphics graphics, int rowSlots, double rowHeight)
    {
        var fill = new XSolidBrush(HeaderFill);
        var pen = new XPen(XColors.Black, 0.65);
        graphics.DrawRectangle(XBrushes.White, 313.7, 361.7, 248.6, 326.4);
        graphics.DrawRectangle(fill, 313.7, 361.7, 248.6, 19.3);
        graphics.DrawRectangle(fill, 313.7, 381.0, 248.6, 16.0);
        graphics.DrawRectangle(fill, 313.7, 667.0, 248.6, 21.1);

        graphics.DrawRectangle(pen, 313.0, 361.0, 250.0, 328.0);
        graphics.DrawLine(pen, 313.0, 381.0, 563.0, 381.0);
        graphics.DrawLine(pen, 313.0, 397.0, 563.0, 397.0);
        for (var index = 1; index <= rowSlots; index++)
        {
            var y = Math.Min(667.0, 397.0 + index * rowHeight);
            graphics.DrawLine(pen, 313.0, y, 563.0, y);
        }
        foreach (var x in new[] { 417.0, 467.0, 517.0 })
            graphics.DrawLine(pen, x, 381.0, x, 667.0);
        graphics.DrawLine(pen, 444.0, 667.0, 444.0, 689.0);

        DrawSingleLine(graphics, "Staff", new XRect(313, 361, 250, 20), 8,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, "Name / Manpower Type", new XRect(313, 381, 104, 16), 6.2,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center, minimumSize: 4.9);
        DrawSingleLine(graphics, "Date", new XRect(417, 381, 50, 16), 6.5,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, "From", new XRect(467, 381, 50, 16), 6.5,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, "To", new XRect(517, 381, 46, 16), 6.5,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        DrawSingleLine(graphics, "Total Staff Hours", new XRect(316, 667, 126, 21), 7,
            XFontStyleEx.Bold, XBrushes.Black, XStringFormats.CenterLeft);
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

                merged.Add(new WorkerWindow(
                    identity.StaffMemberId,
                    identity.Name,
                    identity.ManpowerTypeName,
                    from,
                    to));
                from = window.FromUtc;
                to = window.ToUtc;
            }

            merged.Add(new WorkerWindow(
                identity.StaffMemberId,
                identity.Name,
                identity.ManpowerTypeName,
                from,
                to));
        }

        return merged;
    }

    private static void DrawMaterials(XGraphics graphics, WorkOrderDetailDto workOrder)
    {
        var materials = workOrder.Tasks
            .SelectMany(task => task.Materials)
            .GroupBy(material => material.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new MaterialRow(group.Key, group.Sum(item => item.Quantity)))
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowTops = new[] { 709.9, 727.8, 745.7 };
        for (var index = 0; index < Math.Min(materials.Count, rowTops.Length); index++)
        {
            var material = materials[index];
            var name = index == rowTops.Length - 1 && materials.Count > rowTops.Length
                ? $"{material.Name} (+{materials.Count - rowTops.Length})"
                : material.Name;
            DrawSingleLine(graphics, name, new XRect(34, rowTops[index], 116, 17.4), 6.5,
                XFontStyleEx.Regular, XBrushes.Black, XStringFormats.CenterLeft, minimumSize: 5.2);
            DrawSingleLine(graphics, material.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                new XRect(152, rowTops[index], 48, 17.4), 6.7, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center);
        }
    }

    private static void DrawCustomerAcceptance(XGraphics graphics, ApprovedWorkOrderPrintDto source)
    {
        if (source.CustomerSignatureContent is { Length: > 0 } signature)
            TryDrawSignature(graphics, signature, new XRect(325, 803.5, 143, 21.5));

        if (source.WorkOrder.CustomerSignature?.SignedAtUtc is { } signedAt)
        {
            DrawSingleLine(graphics, signedAt.UtcDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                new XRect(472, 802, 91, 24), 6.8, XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        }
    }

    private static void TryDrawSignature(XGraphics graphics, byte[] content, XRect bounds)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var image = XImage.FromStream(stream);
            var scale = Math.Min(bounds.Width / image.PointWidth, bounds.Height / image.PointHeight);
            var width = image.PointWidth * scale;
            var height = image.PointHeight * scale;
            graphics.DrawImage(image,
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height);
        }
        catch (InvalidOperationException)
        {
            // A missing/corrupt optional signature must not prevent printing the approved record.
        }
        catch (NotSupportedException)
        {
            // The upload policy may expand before PDFsharp supports the new image format.
        }
    }

    private static void DrawOnRuledLines(
        XGraphics graphics,
        IReadOnlyList<string> entries,
        XRect bounds,
        XFont font,
        double lineHeight,
        int maximumLines)
    {
        var wrappedEntries = entries
            .Select(entry => Wrap(graphics, Clean(entry), font, bounds.Width))
            .Where(lines => lines.Count > 0)
            .ToList();
        if (wrappedEntries.Count == 0)
            return;

        if (wrappedEntries.Count > maximumLines)
        {
            var compactLineHeight = bounds.Height / wrappedEntries.Count;
            var compactFontSize = Math.Clamp(compactLineHeight * 0.42, 3.5, font.Size);
            var compactState = graphics.Save();
            graphics.IntersectClip(bounds);
            for (var index = 0; index < wrappedEntries.Count; index++)
            {
                DrawSingleLine(graphics, string.Join(' ', wrappedEntries[index]),
                    new XRect(bounds.X, bounds.Y + index * compactLineHeight, bounds.Width, compactLineHeight),
                    compactFontSize, XFontStyleEx.Regular, new XSolidBrush(TextColor),
                    XStringFormats.CenterLeft, minimumSize: 3.2);
            }
            graphics.Restore(compactState);
            return;
        }

        var allocations = Enumerable.Repeat(1, wrappedEntries.Count).ToArray();
        var remainingLines = maximumLines - wrappedEntries.Count;
        while (remainingLines > 0)
        {
            var allocatedAny = false;
            for (var index = 0; index < wrappedEntries.Count && remainingLines > 0; index++)
            {
                if (allocations[index] >= wrappedEntries[index].Count)
                    continue;
                allocations[index]++;
                remainingLines--;
                allocatedAny = true;
            }

            if (!allocatedAny)
                break;
        }

        var lines = new List<string>();
        for (var index = 0; index < wrappedEntries.Count; index++)
        {
            var wrapped = wrappedEntries[index];
            var allocated = allocations[index];
            lines.AddRange(wrapped.Take(allocated));
            if (allocated < wrapped.Count)
                lines[^1] = Ellipsize(graphics, lines[^1], font, bounds.Width);
        }

        var state = graphics.Save();
        graphics.IntersectClip(bounds);
        for (var index = 0; index < lines.Count; index++)
        {
            graphics.DrawString(lines[index], font, new XSolidBrush(TextColor),
                new XRect(bounds.X, bounds.Y + index * lineHeight, bounds.Width, lineHeight),
                XStringFormats.CenterLeft);
        }
        graphics.Restore(state);
    }

    private static IReadOnlyList<string> Wrap(XGraphics graphics, string value, XFont font, double maximumWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var lines = new List<string>();
        foreach (var paragraph in value.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (graphics.MeasureString(word, font).Width > maximumWidth)
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }

                    var chunks = SplitToken(graphics, word, font, maximumWidth);
                    lines.AddRange(chunks.Take(Math.Max(0, chunks.Count - 1)));
                    current = chunks[^1];
                    continue;
                }

                var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                if (graphics.MeasureString(candidate, font).Width <= maximumWidth)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(current))
                    lines.Add(current);
                current = word;
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current);
        }

        return lines;
    }

    private static IReadOnlyList<string> SplitToken(
        XGraphics graphics,
        string token,
        XFont font,
        double maximumWidth)
    {
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var character in token)
        {
            var candidate = current + character;
            if (current.Length > 0 && graphics.MeasureString(candidate, font).Width > maximumWidth)
            {
                chunks.Add(current);
                current = character.ToString();
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            chunks.Add(current);
        return chunks;
    }

    private static string Ellipsize(XGraphics graphics, string value, XFont font, double maximumWidth)
    {
        const string suffix = "...";
        var candidate = value.TrimEnd();
        while (candidate.Length > 0 && graphics.MeasureString(candidate + suffix, font).Width > maximumWidth)
            candidate = candidate[..^1].TrimEnd();
        return candidate + suffix;
    }

    private static void DrawSingleLine(
        XGraphics graphics,
        string? value,
        XRect bounds,
        double fontSize,
        XFontStyleEx style,
        XBrush brush,
        XStringFormat format,
        double minimumSize = 5.5)
    {
        var text = Clean(value);
        if (text.Length == 0)
            return;

        var size = fontSize;
        var font = new XFont(FontFamily, size, style);
        while (size > minimumSize && graphics.MeasureString(text, font).Width > bounds.Width - 2)
        {
            size = Math.Max(minimumSize, size - 0.4);
            font = new XFont(FontFamily, size, style);
        }

        if (graphics.MeasureString(text, font).Width > bounds.Width - 2)
            text = Ellipsize(graphics, text, font, bounds.Width - 2);

        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static void DrawCheck(XGraphics graphics, double x, double y, double size)
    {
        var pen = new XPen(AccentRed, 1.5);
        graphics.DrawLine(pen, x + 2.4, y + size * 0.52, x + size * 0.43, y + size - 2.1);
        graphics.DrawLine(pen, x + size * 0.43, y + size - 2.1, x + size - 1.9, y + 2.0);
    }

    private static void DrawCheckbox(XGraphics graphics, double x, double y, double size, bool isChecked)
    {
        graphics.DrawRectangle(new XPen(AccentRed, 0.8), x, y, size, size);
        if (isChecked)
            DrawCheck(graphics, x, y, size);
    }

    private static string DisplayFlightNumber(WorkOrderDetailDto workOrder) =>
        string.IsNullOrWhiteSpace(workOrder.CustomerIataCode)
            ? workOrder.ActualFlightNumber
            : $"{workOrder.CustomerIataCode.Trim().ToUpperInvariant()}-{workOrder.ActualFlightNumber}";

    private static string FormatDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string? FormatCompactDateTime(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan value)
    {
        var totalMinutes = (int)Math.Round(value.TotalMinutes);
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}h {totalMinutes % 60:D2}m"
            : $"{totalMinutes}m";
    }

    private static string Normalize(string? value)
    {
        var cleaned = Clean(value).ToLowerInvariant();
        return new string(cleaned.Where(character => char.IsLetterOrDigit(character)).ToArray());
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Trim().Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
    }

    private static string BuildFileName(string? approvalNumber)
    {
        var safeNumber = string.IsNullOrWhiteSpace(approvalNumber)
            ? "approved"
            : string.Concat(approvalNumber.Trim().Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        return $"work-order-{safeNumber}.pdf";
    }

    internal sealed record WorkerWindow(
        Guid StaffMemberId,
        string Name,
        string ManpowerTypeName,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc);

    private sealed record StaffRow(
        string Name,
        string ManpowerTypeName,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc);

    private sealed record MaterialRow(string Name, decimal Quantity);
}
