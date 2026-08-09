using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed class ReturnToRampDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public string? Description { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public List<ReturnToRampServiceDraft> ServiceLines { get; set; } = [];
    public List<ReturnToRampTaskDraft> Tasks { get; set; } = [];

    public ReturnToRampDraft Clone() => new()
    {
        Id = Id,
        FromLocal = FromLocal,
        ToLocal = ToLocal,
        Description = Description,
        RecordedByUserId = RecordedByUserId,
        CreatedAtUtc = CreatedAtUtc,
        ServiceLines = ServiceLines.Select(item => item.Clone()).ToList(),
        Tasks = Tasks.Select(item => item.Clone()).ToList()
    };
}

public sealed class ReturnToRampServiceDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public IEnumerable<Guid> PerformedByStaffMemberIds { get; set; } = [];
    public List<ReturnToRampPersonSnapshot> PerformerSnapshots { get; set; } = [];
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public string? Description { get; set; }
    public List<ReturnToRampAttachmentDraft> Attachments { get; set; } = [];

    public ReturnToRampServiceDraft Clone() => new()
    {
        Id = Id,
        ServiceId = ServiceId,
        ServiceName = ServiceName,
        PerformedByStaffMemberIds = PerformedByStaffMemberIds.ToList(),
        PerformerSnapshots = PerformerSnapshots.ToList(),
        FromLocal = FromLocal,
        ToLocal = ToLocal,
        Description = Description,
        Attachments = Attachments.Select(item => item.Clone()).ToList()
    };
}

public sealed class ReturnToRampTaskDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    public string TaskType { get; set; } = "Major";
    public string? Description { get; set; }
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public IEnumerable<Guid> EmployeeIds { get; set; } = [];
    public List<ReturnToRampPersonSnapshot> EmployeeSnapshots { get; set; } = [];
    public List<ReturnToRampResourceDraft> Tools { get; set; } = [];
    public List<ReturnToRampResourceDraft> Materials { get; set; } = [];
    public List<ReturnToRampResourceDraft> GeneralSupports { get; set; } = [];
    public List<ReturnToRampAttachmentDraft> Attachments { get; set; } = [];

    public ReturnToRampTaskDraft Clone() => new()
    {
        Id = Id,
        TaskType = TaskType,
        Description = Description,
        FromLocal = FromLocal,
        ToLocal = ToLocal,
        EmployeeIds = EmployeeIds.ToList(),
        EmployeeSnapshots = EmployeeSnapshots.ToList(),
        Tools = Tools.Select(item => item.Clone()).ToList(),
        Materials = Materials.Select(item => item.Clone()).ToList(),
        GeneralSupports = GeneralSupports.Select(item => item.Clone()).ToList(),
        Attachments = Attachments.Select(item => item.Clone()).ToList()
    };
}

public sealed class ReturnToRampResourceDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? ItemId { get; set; }
    public string? Name { get; set; }
    public ResourceCalculationType CalculationType { get; set; } = ResourceCalculationType.Quantity;
    public decimal? Quantity { get; set; } = 1;
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }

    public ReturnToRampResourceDraft Clone() => new()
    {
        ItemId = ItemId,
        Name = Name,
        CalculationType = CalculationType,
        Quantity = Quantity,
        FromLocal = FromLocal,
        ToLocal = ToLocal
    };
}

public sealed class ReturnToRampAttachmentDraft
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    public string Kind { get; set; } = "Document";
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[]? Content { get; set; }
    public string? DownloadUrl { get; set; }
    public bool IsPreviewOpen { get; set; }
    public bool IsPending => Content is not null;

    public ReturnToRampAttachmentDraft Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        OriginalFileName = OriginalFileName,
        ContentType = ContentType,
        Size = Size,
        Content = Content?.ToArray(),
        DownloadUrl = DownloadUrl,
        IsPreviewOpen = IsPreviewOpen
    };
}

public sealed record ReturnToRampPersonSnapshot(Guid Id, string FullName, string EmployeeId);

public sealed record ReturnToRampResourceOption(Guid Id, string Name, ResourceCalculationType CalculationType);

public sealed record ReturnToRampEditorResult(ReturnToRampDraft Draft, string? WorkOrderRowVersion);

internal static class ReturnToRampDraftMapper
{
    public static ReturnToRampDraft FromModel(WorkOrderReturnToRampModel source, UserTimeZone timeZone) => new()
    {
        Id = source.Id,
        FromLocal = timeZone.ToLocalDateTime(source.FromUtc),
        ToLocal = timeZone.ToLocalDateTime(source.ToUtc),
        Description = source.Description,
        RecordedByUserId = source.RecordedByUserId,
        CreatedAtUtc = source.CreatedAtUtc,
        ServiceLines = source.ServiceLines.Select(item => FromService(item, timeZone)).ToList(),
        Tasks = source.Tasks.Select(item => FromTask(item, timeZone)).ToList()
    };

    public static IReadOnlyList<WorkOrderServiceLineModel> StandardServiceLines(WorkOrderDetail source) =>
        source.ServiceLines.Where(item => !item.IsReturnToRamp).ToList();

    public static IReadOnlyList<WorkOrderTaskModel> StandardTasks(WorkOrderDetail source) =>
        source.Tasks.Where(item => !item.IsReturnToRamp).ToList();

    public static IReadOnlyList<ReturnToRampDraft> ReturnToRamps(WorkOrderDetail source, UserTimeZone timeZone) =>
        (source.ReturnToRamps ?? []).Select(item => FromModel(item, timeZone)).ToList();

    public static WorkOrderReturnToRampRequestModel ToRequest(ReturnToRampDraft source, UserTimeZone timeZone) => new(
        source.Id,
        timeZone.ToUtc(source.FromLocal)!.Value,
        timeZone.ToUtc(source.ToLocal)!.Value,
        source.Description,
        source.ServiceLines.Select(item => ToServiceRequest(item, timeZone)).ToList(),
        source.Tasks.Select(item => ToTaskRequest(item, timeZone)).ToList());

    public static IReadOnlyList<WorkOrderReturnToRampRequestModel> ToRequests(
        IEnumerable<ReturnToRampDraft> source,
        UserTimeZone timeZone) =>
        source.Select(item => ToRequest(item, timeZone)).ToList();

    private static ReturnToRampServiceDraft FromService(WorkOrderServiceLineModel source, UserTimeZone timeZone) => new()
    {
        Id = source.Id,
        ServiceId = source.ServiceId,
        ServiceName = source.ServiceName,
        PerformedByStaffMemberIds = source.PerformedBy.Select(item => item.StaffMemberId).ToList(),
        PerformerSnapshots = source.PerformedBy
            .Select(item => new ReturnToRampPersonSnapshot(item.StaffMemberId, item.FullName, item.EmployeeId))
            .ToList(),
        FromLocal = timeZone.ToLocalDateTime(source.FromUtc),
        ToLocal = timeZone.ToLocalDateTime(source.ToUtc),
        Description = source.Description,
        Attachments = (source.Attachments ?? []).Select(FromAttachment).ToList()
    };

    private static ReturnToRampTaskDraft FromTask(WorkOrderTaskModel source, UserTimeZone timeZone) => new()
    {
        Id = source.Id,
        TaskType = source.TaskType,
        Description = source.Description,
        FromLocal = timeZone.ToLocalDateTime(source.FromUtc),
        ToLocal = timeZone.ToLocalDateTime(source.ToUtc),
        EmployeeIds = source.Employees.Select(item => item.StaffMemberId).ToList(),
        EmployeeSnapshots = source.Employees
            .Select(item => new ReturnToRampPersonSnapshot(item.StaffMemberId, item.FullName, item.EmployeeId))
            .ToList(),
        Tools = source.Tools.Select(item => FromResource(
            item.ToolId, item.Name, item.CalculationType, item.Quantity, item.FromUtc, item.ToUtc, timeZone)).ToList(),
        Materials = source.Materials.Select(item => FromResource(
            item.MaterialId, item.Name, item.CalculationType, item.Quantity, item.FromUtc, item.ToUtc, timeZone)).ToList(),
        GeneralSupports = source.GeneralSupports.Select(item => FromResource(
            item.GeneralSupportId, item.Name, item.CalculationType, item.Quantity, item.FromUtc, item.ToUtc, timeZone)).ToList(),
        Attachments = source.Attachments.Select(FromAttachment).ToList()
    };

    private static ReturnToRampResourceDraft FromResource(
        Guid id,
        string name,
        ResourceCalculationType calculationType,
        decimal? quantity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        UserTimeZone timeZone) => new()
    {
        ItemId = id,
        Name = name,
        CalculationType = calculationType,
        Quantity = quantity,
        FromLocal = timeZone.ToLocalDateTime(fromUtc),
        ToLocal = timeZone.ToLocalDateTime(toUtc)
    };

    private static ReturnToRampAttachmentDraft FromAttachment(WorkOrderTaskAttachmentModel source) => new()
    {
        Id = source.Id,
        Kind = source.Kind,
        OriginalFileName = source.OriginalFileName,
        ContentType = source.ContentType,
        Size = source.Size
    };

    private static ReturnToRampAttachmentDraft FromAttachment(WorkOrderServiceLineAttachmentModel source) => new()
    {
        Id = source.Id,
        Kind = source.Kind,
        OriginalFileName = source.OriginalFileName,
        ContentType = source.ContentType,
        Size = source.Size
    };

    private static WorkOrderServiceLineRequestModel ToServiceRequest(
        ReturnToRampServiceDraft source,
        UserTimeZone timeZone) => new(
        source.ServiceId!.Value,
        source.PerformedByStaffMemberIds.ToList(),
        timeZone.ToUtc(source.FromLocal)!.Value,
        timeZone.ToUtc(source.ToLocal)!.Value,
        source.Description,
        Id: source.Id,
        Attachments: source.Attachments.Where(item => item.IsPending).Select(item => new WorkOrderServiceLineAttachmentRequestModel(
            item.Kind,
            Convert.ToBase64String(item.Content!),
            item.OriginalFileName,
            item.ContentType)).ToList());

    private static WorkOrderTaskRequestModel ToTaskRequest(
        ReturnToRampTaskDraft source,
        UserTimeZone timeZone) => new(
        source.Id,
        source.TaskType,
        source.Description,
        timeZone.ToUtc(source.FromLocal)!.Value,
        timeZone.ToUtc(source.ToLocal)!.Value,
        source.EmployeeIds.ToList(),
        source.Tools.Where(item => item.ItemId.HasValue).Select(item => new WorkOrderTaskToolRequestModel(
            item.ItemId!.Value,
            item.CalculationType == ResourceCalculationType.Quantity ? item.Quantity : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.FromLocal) : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.ToLocal) : null)).ToList(),
        source.Materials.Where(item => item.ItemId.HasValue).Select(item => new WorkOrderTaskMaterialRequestModel(
            item.ItemId!.Value,
            item.CalculationType == ResourceCalculationType.Quantity ? item.Quantity : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.FromLocal) : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.ToLocal) : null)).ToList(),
        source.GeneralSupports.Where(item => item.ItemId.HasValue).Select(item => new WorkOrderTaskGeneralSupportRequestModel(
            item.ItemId!.Value,
            item.CalculationType == ResourceCalculationType.Quantity ? item.Quantity : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.FromLocal) : null,
            item.CalculationType == ResourceCalculationType.Duration ? timeZone.ToUtc(item.ToLocal) : null)).ToList(),
        source.Attachments.Where(item => item.IsPending).Select(item => new WorkOrderTaskAttachmentRequestModel(
            item.Kind,
            Convert.ToBase64String(item.Content!),
            item.OriginalFileName,
            item.ContentType)).ToList());
}

internal static class ReturnToRampDraftValidation
{
    public static List<string> Validate(ReturnToRampDraft draft, UserTimeZone timeZone, string prefix = "Return to ramp")
    {
        var messages = new List<string>();
        if (IsMissing(draft.FromLocal) || IsMissing(draft.ToLocal))
            messages.Add($"{prefix} requires From and To times.");
        else if (draft.ToLocal < draft.FromLocal)
            messages.Add($"{prefix} To time cannot be before From time.");
        if (!string.IsNullOrWhiteSpace(draft.Description) && draft.Description.Trim().Length > 2000)
            messages.Add($"{prefix} description must be at most 2000 characters.");
        if (draft.ServiceLines.Count == 0 && draft.Tasks.Count == 0)
            messages.Add($"{prefix} requires at least one service or task.");
        AddZoneValidation(messages, $"{prefix} From", draft.FromLocal, timeZone);
        AddZoneValidation(messages, $"{prefix} To", draft.ToLocal, timeZone);

        foreach (var (service, index) in draft.ServiceLines.Select((item, index) => (item, index + 1)))
        {
            var label = $"{prefix} service {index}";
            if (service.ServiceId is null || !service.PerformedByStaffMemberIds.Any() ||
                IsMissing(service.FromLocal) || IsMissing(service.ToLocal))
            {
                messages.Add($"{label} needs a service, at least one performer, From, and To time.");
            }
            if (!IsMissing(service.FromLocal) && !IsMissing(service.ToLocal) && service.ToLocal < service.FromLocal)
                messages.Add($"{label} To time cannot be before From time.");
            if (!string.IsNullOrWhiteSpace(service.Description) && service.Description.Trim().Length > 2000)
                messages.Add($"{label} description must be at most 2000 characters.");
            ValidateInsideOccurrence(messages, label, service.FromLocal, service.ToLocal, draft);
            AddZoneValidation(messages, $"{label} From", service.FromLocal, timeZone);
            AddZoneValidation(messages, $"{label} To", service.ToLocal, timeZone);
        }

        foreach (var (task, index) in draft.Tasks.Select((item, index) => (item, index + 1)))
        {
            var label = $"{prefix} task {index}";
            if (string.IsNullOrWhiteSpace(task.TaskType) || !task.EmployeeIds.Any() ||
                IsMissing(task.FromLocal) || IsMissing(task.ToLocal))
            {
                messages.Add($"{label} needs Major/Minor, at least one employee, From, and To time.");
            }
            if (!IsMissing(task.FromLocal) && !IsMissing(task.ToLocal) && task.ToLocal < task.FromLocal)
                messages.Add($"{label} To time cannot be before From time.");
            if (!string.IsNullOrWhiteSpace(task.Description) && task.Description.Trim().Length > 2000)
                messages.Add($"{label} description must be at most 2000 characters.");
            ValidateInsideOccurrence(messages, label, task.FromLocal, task.ToLocal, draft);
            AddZoneValidation(messages, $"{label} From", task.FromLocal, timeZone);
            AddZoneValidation(messages, $"{label} To", task.ToLocal, timeZone);
            ValidateResources(messages, task.Tools, task, label, "tool", timeZone);
            ValidateResources(messages, task.Materials, task, label, "material", timeZone);
            ValidateResources(messages, task.GeneralSupports, task, label, "general support", timeZone);
        }

        return messages.Distinct().ToList();
    }

    private static void ValidateResources(
        List<string> messages,
        IReadOnlyList<ReturnToRampResourceDraft> resources,
        ReturnToRampTaskDraft task,
        string taskLabel,
        string resourceLabel,
        UserTimeZone timeZone)
    {
        if (resources.Any(item => item.ItemId is null))
            messages.Add($"Every {taskLabel} {resourceLabel} row needs an item.");
        if (resources.Where(item => item.ItemId.HasValue).GroupBy(item => item.ItemId).Any(group => group.Count() > 1))
            messages.Add($"Duplicate {resourceLabel} rows are not allowed in {taskLabel}.");

        foreach (var (resource, index) in resources.Select((item, index) => (item, index + 1)))
        {
            var label = $"{taskLabel} {resourceLabel} {index}";
            if (resource.CalculationType == ResourceCalculationType.Quantity)
            {
                if (resource.Quantity is null or <= 0)
                    messages.Add($"{label} quantity must be greater than zero.");
                else if (resource.Quantity > 9999999999999999.99m || decimal.Round(resource.Quantity.Value, 2) != resource.Quantity.Value)
                    messages.Add($"{label} quantity supports up to 16 whole digits and 2 decimal places.");
                continue;
            }

            if (IsMissing(resource.FromLocal))
                messages.Add($"{label} requires a From time.");
            if (!IsMissing(resource.FromLocal) && !IsMissing(resource.ToLocal) && resource.ToLocal < resource.FromLocal)
                messages.Add($"{label} To time cannot be before From time.");
            if (!IsMissing(task.FromLocal) && !IsMissing(resource.FromLocal) && resource.FromLocal < task.FromLocal)
                messages.Add($"{label} From time cannot be before the task From time.");
            if (!IsMissing(task.ToLocal) && !IsMissing(resource.ToLocal) && resource.ToLocal > task.ToLocal)
                messages.Add($"{label} To time cannot be after the task To time.");
            AddZoneValidation(messages, $"{label} From", resource.FromLocal, timeZone);
            AddZoneValidation(messages, $"{label} To", resource.ToLocal, timeZone);
        }
    }

    private static void ValidateInsideOccurrence(
        List<string> messages,
        string label,
        DateTime? from,
        DateTime? to,
        ReturnToRampDraft occurrence)
    {
        if (!IsMissing(occurrence.FromLocal) && !IsMissing(from) && from < occurrence.FromLocal)
            messages.Add($"{label} From time cannot be before the return-to-ramp From time.");
        if (!IsMissing(occurrence.ToLocal) && !IsMissing(to) && to > occurrence.ToLocal)
            messages.Add($"{label} To time cannot be after the return-to-ramp To time.");
    }

    private static void AddZoneValidation(
        List<string> messages,
        string label,
        DateTime? value,
        UserTimeZone timeZone)
    {
        if (!IsMissing(value) && !timeZone.TryToUtc(value, out _, out var error))
            messages.Add($"{label}: {error}");
    }

    private static bool IsMissing(DateTime? value) => value is null || value.Value == default;
}

internal static class CompletionWorkOrderWizard
{
    public const int DetailsStep = 0;
    public const int ServiceLinesStep = 1;
    public const int TasksStep = 2;
    public const int ReturnToRampsStep = 3;
    public const int SignatureStep = 4;

    public static IReadOnlyList<string> EditorLabels { get; } =
        ["Details", "Service lines", "Tasks", "Return to ramps", "Signature"];

    public static IReadOnlyList<string> AdHocLabels { get; } =
        ["Flight", "Service lines", "Tasks", "Return to ramps", "Signature"];
}

internal static class ReturnToRampPortalPolicy
{
    public static bool CanRecordForFlightStatus(string status) => status is "InProgress" or "Completed";
}

internal static class WorkOrderAttachmentMutation
{
    public static string ApplyPersistedDeletion<TAttachment>(
        ICollection<TAttachment> attachments,
        TAttachment attachment,
        string refreshedRowVersion)
    {
        attachments.Remove(attachment);
        return refreshedRowVersion;
    }
}

internal static class ReturnToRampAttachmentValidation
{
    public const int MaxAttachments = 10;

    public static IReadOnlyList<string> Validate(ReturnToRampDraft draft)
    {
        var messages = new List<string>();
        var groups = draft.ServiceLines
            .Select((item, index) => (item.Attachments, Label: $"Return to ramp service {index + 1}"))
            .Concat(draft.Tasks.Select((item, index) => (item.Attachments, Label: $"Return to ramp task {index + 1}")));

        foreach (var (attachments, label) in groups)
        {
            if (attachments.Count > MaxAttachments)
                messages.Add($"{label} can have at most {MaxAttachments} attachments.");
            foreach (var attachment in attachments.Where(item => item.IsPending))
            {
                if (attachment.Size <= 0 || attachment.Size > MaxBytes(attachment.Kind))
                    messages.Add($"{label} contains an empty or oversized {attachment.Kind.ToLowerInvariant()} attachment.");
                if (!IsAllowedContentType(attachment.Kind, attachment.ContentType))
                    messages.Add($"{label} contains an unsupported {attachment.Kind.ToLowerInvariant()} attachment type.");
            }
        }

        return messages.Distinct().ToList();
    }

    public static bool IsAllowedContentType(string kind, string contentType) => kind switch
    {
        "Image" => contentType is "image/png" or "image/jpeg" or "image/webp",
        "Voice" => contentType is "audio/mp4" or "audio/m4a" or "audio/mpeg" or "audio/mp3" or "audio/ogg" or "audio/webm",
        "Document" => contentType == "application/pdf",
        _ => false
    };

    public static long MaxBytes(string kind) => kind switch
    {
        "Image" => 10 * 1024 * 1024,
        "Voice" => 25 * 1024 * 1024,
        _ => 20 * 1024 * 1024
    };
}
