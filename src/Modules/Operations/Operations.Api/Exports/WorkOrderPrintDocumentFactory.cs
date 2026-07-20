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
    private const string FontFamily = PdfDocumentAssets.FontFamily;

    private static readonly XColor TextColor = XColor.FromArgb(0x23, 0x1F, 0x20);
    private static readonly XColor AccentRed = XColor.FromArgb(0xED, 0x1C, 0x24);

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
        var bold = new XFont(FontFamily, 7.2, XFontStyleEx.Bold);

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

        DrawRequestedServiceChecks(graphics, source);
        DrawFlightTypeCheck(graphics, workOrder.OperationTypeName);
        DrawReturnToRamp(graphics, workOrder);
        DrawFlightTimes(graphics, workOrder);
        DrawCorrectiveAction(graphics, workOrder, regular);

        if (workOrder.Tasks.Any(task => task.TaskType.Equals("Major", StringComparison.OrdinalIgnoreCase)))
            DrawCheck(graphics, 171.0, 364.3, 12.1);
        if (workOrder.Tasks.Any(task => task.TaskType.Equals("Minor", StringComparison.OrdinalIgnoreCase)))
            DrawCheck(graphics, 170.7, 382.0, 12.1);

        DrawTechnicians(graphics, workOrder, bold);
        DrawMaterials(graphics, workOrder);
        DrawCustomerAcceptance(graphics, source);
    }

    private static void DrawRequestedServiceChecks(XGraphics graphics, ApprovedWorkOrderPrintDto source)
    {
        var values = source.PlannedServiceNames.Select(Normalize).ToList();
        var matched = false;

        matched |= DrawCheckWhen(graphics, values.Any(value => value.Contains("headset", StringComparison.Ordinal)), 415.2, 133.2);
        matched |= DrawCheckWhen(graphics, values.Any(value => value.Contains("transit", StringComparison.Ordinal)), 415.2, 151.8);
        matched |= DrawCheckWhen(graphics, values.Any(value => value.Contains("daily", StringComparison.Ordinal)), 415.2, 170.4);
        matched |= DrawCheckWhen(graphics, values.Any(value => value.Contains("weekly", StringComparison.Ordinal)), 415.2, 189.0);

        var onCall = source.IsOnCall || values.Any(value => value.Contains("oncall", StringComparison.Ordinal));
        matched |= DrawCheckWhen(graphics, onCall, 356.2, 207.3);

        if (!matched && values.Count > 0)
            DrawCheck(graphics, 415.2, 207.3, 12.1);
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
        var windows = workOrder.ServiceLines
            .Where(line => line.IsReturnToRamp)
            .Select(line => (line.FromUtc, line.ToUtc))
            .Concat(workOrder.Tasks.Where(task => task.IsReturnToRamp).Select(task => (task.FromUtc, task.ToUtc)))
            .OrderBy(window => window.FromUtc)
            .ToList();

        if (windows.Count == 0)
            return;

        // The domain records that the work returned to ramp, but it does not distinguish Taxi from
        // Flight. Leave both subtype boxes blank instead of asserting a fact that was not captured.
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
        var entries = new List<string>();
        foreach (var line in workOrder.ServiceLines.OrderBy(line => line.FromUtc).ThenBy(line => line.Id))
        {
            entries.Add(string.IsNullOrWhiteSpace(line.Description)
                ? line.ServiceName
                : $"{line.ServiceName}: {line.Description}");
        }

        foreach (var task in workOrder.Tasks.OrderBy(task => task.FromUtc).ThenBy(task => task.Id))
        {
            var title = task.TaskType.Equals("Minor", StringComparison.OrdinalIgnoreCase) ||
                        task.TaskType.Equals("Major", StringComparison.OrdinalIgnoreCase)
                ? task.TaskType
                : "Task";
            entries.Add(string.IsNullOrWhiteSpace(task.Description) ? title : $"{title}: {task.Description}");
        }

        if (!string.IsNullOrWhiteSpace(workOrder.Remarks))
            entries.Add($"Remarks: {workOrder.Remarks}");

        DrawOnRuledLines(graphics, entries, new XRect(35, 398.2, 272, 289.5), font, 19.42, 15);
    }

    private static void DrawTechnicians(XGraphics graphics, WorkOrderDetailDto workOrder, XFont font)
    {
        var windows = MergeWorkerWindows(workOrder.ServiceLines
            .Select(line => new WorkerWindow(line.PerformedByName, line.FromUtc, line.ToUtc))
            .Concat(workOrder.Tasks.SelectMany(task => task.Employees.Select(employee =>
                new WorkerWindow(employee.FullName, task.FromUtc, task.ToUtc))))
            .Where(window => !string.IsNullOrWhiteSpace(window.Name))
            .ToList())
            .OrderBy(window => window.FromUtc)
            .ThenBy(window => window.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowBounds = new[] { (Top: 397.1, Bottom: 453.3), (Top: 454.0, Bottom: 513.3) };
        for (var index = 0; index < Math.Min(windows.Count, rowBounds.Length); index++)
        {
            var window = windows[index];
            var name = index == rowBounds.Length - 1 && windows.Count > rowBounds.Length
                ? $"{window.Name} (+{windows.Count - rowBounds.Length})"
                : window.Name;
            var bounds = rowBounds[index];

            DrawSingleLine(graphics, name, new XRect(315, bounds.Top, 101, bounds.Bottom - bounds.Top), 6.8,
                XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center, minimumSize: 5.2);
            DrawSingleLine(graphics, window.FromUtc.UtcDateTime.ToString("dd/MM/yy", CultureInfo.InvariantCulture),
                new XRect(418, bounds.Top, 48, bounds.Bottom - bounds.Top), 6.2, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center, minimumSize: 5.2);
            DrawSingleLine(graphics, window.FromUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                new XRect(467, bounds.Top, 49, bounds.Bottom - bounds.Top), 6.6, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center);
            DrawSingleLine(graphics, window.ToUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                new XRect(517, bounds.Top, 45, bounds.Bottom - bounds.Top), 6.6, XFontStyleEx.Bold,
                XBrushes.Black, XStringFormats.Center);
        }

        var total = windows.Aggregate(TimeSpan.Zero, (sum, window) =>
            sum + (window.ToUtc >= window.FromUtc ? window.ToUtc - window.FromUtc : TimeSpan.Zero));
        if (total > TimeSpan.Zero)
        {
            DrawSingleLine(graphics, FormatDuration(total), new XRect(444, 667.5, 118, 20.5), 7,
                XFontStyleEx.Bold, XBrushes.Black, XStringFormats.Center);
        }
    }

    internal static IReadOnlyList<WorkerWindow> MergeWorkerWindows(IReadOnlyList<WorkerWindow> source)
    {
        var merged = new List<WorkerWindow>();
        foreach (var group in source.GroupBy(window => window.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(window => window.FromUtc)
                .ThenBy(window => window.ToUtc)
                .ToList();
            if (ordered.Count == 0)
                continue;

            var from = ordered[0].FromUtc;
            var to = ordered[0].ToUtc;
            foreach (var window in ordered.Skip(1))
            {
                if (window.FromUtc <= to)
                {
                    if (window.ToUtc > to)
                        to = window.ToUtc;
                    continue;
                }

                merged.Add(new WorkerWindow(group.Key, from, to));
                from = window.FromUtc;
                to = window.ToUtc;
            }

            merged.Add(new WorkerWindow(group.Key, from, to));
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
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            foreach (var line in Wrap(graphics, Clean(entry), font, bounds.Width))
            {
                if (lines.Count == maximumLines)
                    break;
                lines.Add(line);
            }

            if (lines.Count == maximumLines)
                break;
        }

        if (lines.Count == maximumLines && entries.Count > 0)
            lines[^1] = Ellipsize(graphics, lines[^1], font, bounds.Width);

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

    private static bool DrawCheckWhen(XGraphics graphics, bool condition, double x, double y)
    {
        if (condition)
            DrawCheck(graphics, x, y, 12.1);
        return condition;
    }

    private static void DrawCheck(XGraphics graphics, double x, double y, double size)
    {
        var pen = new XPen(AccentRed, 1.5);
        graphics.DrawLine(pen, x + 2.4, y + size * 0.52, x + size * 0.43, y + size - 2.1);
        graphics.DrawLine(pen, x + size * 0.43, y + size - 2.1, x + size - 1.9, y + 2.0);
    }

    private static string DisplayFlightNumber(WorkOrderDetailDto workOrder) =>
        string.IsNullOrWhiteSpace(workOrder.CustomerIataCode)
            ? workOrder.ActualFlightNumber
            : $"{workOrder.CustomerIataCode.Trim().ToUpperInvariant()}-{workOrder.ActualFlightNumber}";

    private static string FormatDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

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

    internal sealed record WorkerWindow(string Name, DateTimeOffset FromUtc, DateTimeOffset ToUtc);
    private sealed record MaterialRow(string Name, decimal Quantity);
}
