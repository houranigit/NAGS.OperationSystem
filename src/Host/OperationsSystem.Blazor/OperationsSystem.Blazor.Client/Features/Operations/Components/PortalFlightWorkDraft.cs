using System.Text.Json;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed class PortalOperationTime(StationTimeZone zone, DateTimeOffset? original = null)
{
    private readonly DateTime? originalLocal = original is { } time ? zone.ToLocalDateTime(time) : null;

    public bool HasOriginal => original.HasValue;
    public bool HasChanged => Local != originalLocal;
    public DateTime? Local { get; set; } = original is { } value ? zone.ToLocalDateTime(value) : null;

    public DateTimeOffset? Read(string label, List<string> errors, bool required = false) => Read(label, errors.Add, required);

    public DateTimeOffset? Read(string label, PortalFlightValidation errors, string field, bool required = false) =>
        Read(label, message => errors.Add(field, message), required);

    internal bool TryReadUtc(out DateTimeOffset? utc, out string? error)
    {
        if (Local != originalLocal)
            return zone.TryToUtc(Local, out utc, out error);

        utc = original;
        error = null;
        return true;
    }

    private DateTimeOffset? Read(string label, Action<string> addError, bool required)
    {
        if (Local is null && required)
        {
            addError($"{label} is required.");
            return null;
        }

        if (TryReadUtc(out var utc, out var error))
            return utc;

        addError($"{label}. {error}");
        return null;
    }
}

public sealed partial class PortalWorkDraft(Guid id, bool isTask, StationTimeZone zone, DateTimeOffset? start = null, DateTimeOffset? end = null)
{
    internal const int MaxAttachments = 10;
    private string? baseline;

    public Guid Id { get; } = id;
    public bool IsTask { get; } = isTask;
    public bool IsNew => baseline is null;
    public bool IsPlanned { get; private init; }
    public bool CanEdit { get; private init; } = true;
    public bool CanSetEnd { get; private init; } = true;
    public bool CanDelete { get; private init; } = true;
    public bool CanChangeKind { get; private init; } = true;
    public Guid? CreatedByUserId { get; private init; }
    public string? CreatedByDisplayName { get; private init; }
    public DateTimeOffset? CreatedAtUtc { get; private init; }
    public string? UpdatedByDisplayName { get; private init; }
    public DateTimeOffset? UpdatedAtUtc { get; private init; }
    internal bool CorrectionsUnlocked { get; set; }
    public bool Deleted { get; set; }
    public bool IsReadingFile { get; set; }
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; private init; }
    public int TaskType { get; set; }
    public Guid? AtaChapterId { get; set; }
    public string? AtaChapterLabel { get; private init; }
    public PortalOperationTime Start { get; } = new(zone, start);
    public PortalOperationTime End { get; } = new(zone, end);
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public List<PortalPerformerDraft> Performers { get; private init; } = [];
    public List<PortalResourceDraft> Tools { get; private init; } = [];
    public List<PortalResourceDraft> Materials { get; private init; } = [];
    public List<PortalResourceDraft> GeneralSupports { get; private init; } = [];
    public List<PortalAttachmentDraft> Attachments { get; private init; } = [];

    private string CurrentState() => JsonSerializer.Serialize(new
    {
        ServiceId, TaskType, AtaChapterId, Start = Start.Local, End = End.Local, Reference, Notes,
        Performers, Tools, Materials, GeneralSupports, Attachments = Attachments.Select(file => file.Id)
    });

    public bool HasChanges() => Deleted || (IsNew ? !IsPlanned || Start.Local.HasValue || Attachments.Count > 0 : baseline != CurrentState());

    private PortalWorkDraft Capture()
    {
        baseline = CurrentState();
        return this;
    }

    public static PortalWorkDraft FromPlan(PlannedServiceModel planned, Guid? staffId, PortalCatalogChoice? staff, StationTimeZone zone) =>
        new(Guid.NewGuid(), false, zone)
        {
            IsPlanned = true, ServiceId = planned.ServiceId, ServiceName = planned.Name,
            Performers = staffId is null ? [] : [new(Guid.NewGuid(), zone)
            {
                StaffMemberId = staffId,
                DisplayName = staff?.Label ?? "You",
                FullName = staff?.FullName ?? "You"
            }]
        };

    public static PortalWorkDraft From(PortalFlightService source, StationTimeZone zone) => new PortalWorkDraft(
        source.Id, false, zone, source.StartUtc, source.EndUtc)
    {
        ServiceId = source.ServiceId,
        ServiceName = source.ServiceName,
        Reference = source.CustomerReferenceNumber,
        Notes = source.Notes,
        CanEdit = source.CanEdit,
        CanSetEnd = source.CanSetEnd,
        CanDelete = source.CanDelete,
        CanChangeKind = source.CanChangeService,
        CreatedByUserId = source.CreatedByUserId,
        CreatedByDisplayName = source.CreatedByDisplayName,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedByDisplayName = source.UpdatedByDisplayName,
        UpdatedAtUtc = source.UpdatedAtUtc,
        Performers = ReadPerformerPeriods(source.Performers, zone),
        Attachments = source.Attachments.Select(PortalAttachmentDraft.From).ToList()
    }.Capture();

    public static PortalWorkDraft From(PortalFlightTask source, StationTimeZone zone) => new PortalWorkDraft(
        source.Id, true, zone, source.StartUtc, source.EndUtc)
    {
        TaskType = source.TaskType,
        AtaChapterId = source.AtaChapterId,
        AtaChapterLabel = source.AtaChapterId is null ? null : $"{source.AtaChapterCode} · {source.AtaChapterTitle}",
        Reference = source.CustomerReferenceNumber,
        Notes = source.Notes,
        CanEdit = source.CanEdit,
        CanSetEnd = source.CanSetEnd,
        CanDelete = source.CanDelete,
        CanChangeKind = source.CanChangeType,
        CreatedByUserId = source.CreatedByUserId,
        CreatedByDisplayName = source.CreatedByDisplayName,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedByDisplayName = source.UpdatedByDisplayName,
        UpdatedAtUtc = source.UpdatedAtUtc,
        Performers = ReadPerformerPeriods(source.Performers, zone),
        Attachments = source.Attachments.Select(PortalAttachmentDraft.From).ToList(),
        Tools = source.Tools.Select(item => PortalResourceDraft.From(item, zone)).ToList(),
        Materials = source.Materials.Select(item => PortalResourceDraft.From(item, zone)).ToList(),
        GeneralSupports = source.GeneralSupports.Select(item => PortalResourceDraft.From(item, zone)).ToList()
    }.Capture();

    private static List<PortalPerformerDraft> ReadPerformerPeriods(IEnumerable<PortalFlightPerformer> performers, StationTimeZone zone) =>
        performers.GroupBy(item => item.StaffMemberId)
            .SelectMany(employee => employee.OrderBy(item => item.StartUtc))
            .Select(item => PortalPerformerDraft.From(item, zone)).ToList();

    public PortalServiceChange ToServiceChange(int index, PortalFlightValidation errors, bool closing = false)
    {
        if (Deleted)
            return new(Id, null);

        var label = $"Service {index}";
        var (from, to, performers, attachments) = ReadWork(label, errors, closing);
        if (ServiceId is null || ServiceId == Guid.Empty)
            errors.Add(Field("service"), $"{label} needs a service.");
        return new(Id, new(ServiceId.GetValueOrDefault(), from.GetValueOrDefault(), to, Trim(Reference), Trim(Notes), performers, attachments));
    }

    public PortalTaskChange ToTaskChange(int index, PortalFlightValidation errors, bool closing = false)
    {
        if (Deleted)
            return new(Id, null);

        var label = $"Task {index}";
        var (from, to, performers, attachments) = ReadWork(label, errors, closing);
        if (TaskType is not (0 or 1))
            errors.Add(Field("type"), $"{label} needs a valid task type.");
        if (to.HasValue && DurationResources().Any(item => item.End.Local is null))
            errors.Add(Field("end"), $"Close every duration resource period before closing {label}.");
        return new(Id, new(TaskType, AtaChapterId, from.GetValueOrDefault(), to, Trim(Reference), Trim(Notes), performers,
            ReadResources(Tools, "tool"), ReadResources(Materials, "material"),
            ReadResources(GeneralSupports, "general support"), attachments));

        PortalFlightResourceRequest[] ReadResources(List<PortalResourceDraft> resources, string kind)
        {
            var selections = new HashSet<Guid>();
            return resources.Select((resource, resourceIndex) =>
            {
                if (!selections.Add(resource.ResourceId))
                    errors.Add(PortalFlightValidation.Field(resource.Id, "resource"), $"{label} already uses this {kind}.");
                var resourceLabel = $"{label} {kind} {resourceIndex + 1}";
                var input = resource.ToRequest(resourceLabel, errors, closing || to.HasValue);
                if (resource.CalculationType is ResourceCalculationType.Duration)
                    ValidateContainedPeriod(resource.Id, resourceLabel, input.StartUtc, input.EndUtc, label, from, to, errors);
                return input;
            }).ToArray();
        }
    }

    private (DateTimeOffset? Start, DateTimeOffset? End, PortalFlightPerformerRequest[] Performers, PortalFlightAttachmentRequest[] Attachments)
        ReadWork(string label, PortalFlightValidation errors, bool closing)
    {
        var from = Start.Read($"{label} start", errors, Field("start"), required: true);
        var to = End.Read($"{label} end", errors, Field("end"), required: closing);
        if (from > to)
            errors.Add(Field("end"), $"{label} end cannot be before its start.");
        if (Performers.Count == 0)
            errors.Add(Field("performers"), $"{label} needs at least one performer.");
        if (Trim(Reference)?.Length > 200)
            errors.Add(Field("reference"), $"{label} customer reference must be at most 200 characters.");
        if (Trim(Notes)?.Length > 2000)
            errors.Add(Field("notes"), $"{label} remarks must be at most 2,000 characters.");
        if (Attachments.Count > MaxAttachments)
            errors.Add(Field("attachments"), $"{label} can have at most {MaxAttachments} attachments.");
        if (to.HasValue && Performers.Any(item => item.End.Local is null))
            errors.Add(Field("end"), $"Close every performer period before closing {label}.");
        var performerInputs = Performers.Select((item, index) =>
        {
            var performerLabel = $"{label} performer {index + 1}";
            var input = item.ToRequest(performerLabel, errors, closing || to.HasValue, IsTask);
            ValidateContainedPeriod(item.Id, performerLabel, input.StartUtc == default ? null : input.StartUtc, input.EndUtc,
                label, from, to, errors);
            return input;
        }).ToArray();
        ValidatePerformerPeriods(performerInputs, label, errors);
        return (from, to, performerInputs, Attachments.Select(item => item.ToRequest()).ToArray());
    }

    private string Field(string name) => PortalFlightValidation.Field(Id, name);

    public static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PortalPerformerDraft(Guid id, StationTimeZone zone, DateTimeOffset? start = null, DateTimeOffset? end = null)
{
    public Guid Id { get; } = id;
    internal bool IsSaved { get; private init; }
    public Guid? StaffMemberId { get; set; }
    public string? DisplayName { get; init; }
    public string? FullName { get; init; }
    public PortalOperationTime Start { get; } = new(zone, start);
    public PortalOperationTime End { get; } = new(zone, end);
    public string? Notes { get; set; }

    public static PortalPerformerDraft From(PortalFlightPerformer source, StationTimeZone zone) =>
        new(source.Id, zone, source.StartUtc, source.EndUtc)
        {
            IsSaved = true,
            StaffMemberId = source.StaffMemberId,
            DisplayName = $"{source.FullName} · {source.EmployeeId}",
            FullName = source.FullName,
            Notes = source.Notes
        };

    public PortalFlightPerformerRequest ToRequest(string label, PortalFlightValidation errors, bool closing, bool isTask)
    {
        if (StaffMemberId is null || StaffMemberId == Guid.Empty)
            errors.Add(PortalFlightValidation.Field(Id, "employee"), $"{label} needs an employee.");

        var from = Start.Read($"{label} start", errors, PortalFlightValidation.Field(Id, "start"), required: true);
        var endField = PortalFlightValidation.Field(Id, "end");
        var to = End.Read($"{label} end", errors, endField, required: closing);
        if (from > to)
            errors.Add(endField, $"{label} end cannot be before its start.");
        if (isTask && PortalWorkDraft.Trim(Notes)?.Length > 2000)
            errors.Add(PortalFlightValidation.Field(Id, "notes"), $"{label} remarks must be at most 2,000 characters.");
        return new(Id, StaffMemberId.GetValueOrDefault(), from.GetValueOrDefault(), to, PortalWorkDraft.Trim(Notes));
    }
}

public sealed class PortalResourceDraft(Guid id, Guid resourceId, string name, ResourceCalculationType calculationType,
    StationTimeZone zone, DateTimeOffset? start = null, DateTimeOffset? end = null)
{
    public Guid Id { get; } = id;
    internal bool IsSaved { get; private init; }
    public Guid ResourceId { get; } = resourceId;
    public string Name { get; } = name;
    public ResourceCalculationType CalculationType { get; } = calculationType;
    public decimal? Quantity { get; set; }
    public PortalOperationTime Start { get; } = new(zone, start);
    public PortalOperationTime End { get; } = new(zone, end);
    public string? Notes { get; set; }

    public static PortalResourceDraft From(PortalFlightResource source, StationTimeZone zone) =>
        new(source.Id, source.ResourceId, source.Name, source.CalculationType, zone, source.StartUtc, source.EndUtc)
        {
            IsSaved = true, Quantity = source.Quantity, Notes = source.Notes
        };

    public PortalFlightResourceRequest ToRequest(string label, PortalFlightValidation errors, bool closing)
    {
        if (PortalWorkDraft.Trim(Notes)?.Length > 2000)
            errors.Add(PortalFlightValidation.Field(Id, "notes"), $"{label} remarks must be at most 2,000 characters.");
        if (ResourceId == Guid.Empty || CalculationType is not (ResourceCalculationType.Quantity or ResourceCalculationType.Duration))
        {
            errors.Add(PortalFlightValidation.Field(Id, "resource"), $"{label} needs a valid resource.");
            return new(Id, Guid.Empty, null, null, null, PortalWorkDraft.Trim(Notes));
        }
        if (CalculationType is ResourceCalculationType.Quantity)
        {
            if (Quantity is null or <= 0)
                errors.Add(PortalFlightValidation.Field(Id, "quantity"), $"{label} quantity must be greater than zero.");
            return new(Id, ResourceId, Quantity, null, null, PortalWorkDraft.Trim(Notes));
        }

        var from = Start.Read($"{label} start", errors, PortalFlightValidation.Field(Id, "start"), required: true);
        var endField = PortalFlightValidation.Field(Id, "end");
        var to = End.Read($"{label} end", errors, endField, required: closing);
        if (from > to)
            errors.Add(endField, $"{label} end cannot be before its start.");
        return new(Id, ResourceId, null, from, to, PortalWorkDraft.Trim(Notes));
    }
}

public sealed record PortalAttachmentDraft(Guid Id, int Kind, string FileName, string ContentType,
    long SizeBytes, DateTimeOffset? UploadedAtUtc, byte[]? Bytes)
{
    public static PortalAttachmentDraft From(PortalFlightAttachment value) =>
        new(value.Id, value.Kind, value.FileName, value.ContentType, value.SizeBytes, value.UploadedAtUtc, null);

    public PortalFlightAttachmentRequest ToRequest() => new(Id, Kind, FileName, ContentType, Bytes);
}

public sealed record PortalCatalogChoice(Guid Id, string Label, ResourceCalculationType? CalculationType = null)
{
    public string? FullName { get; init; }
    public string? ManpowerTypeName { get; init; }
    public bool IsDisabled { get; init; }
}

public sealed record PortalWorkLookups(
    IReadOnlyList<PortalCatalogChoice> Aircraft, IReadOnlyList<PortalCatalogChoice> Services,
    IReadOnlyList<PortalCatalogChoice> Staff, IReadOnlyList<PortalCatalogChoice> Chapters,
    IReadOnlyList<PortalCatalogChoice> Tools, IReadOnlyList<PortalCatalogChoice> Materials,
    IReadOnlyList<PortalCatalogChoice> Supports, bool HasUnavailableCatalogs)
{
    public static async Task<PortalWorkLookups> LoadAsync(MasterDataApiClient api, PortalFlightOperations flight,
        bool occurrenceOnly, bool administratorEntry, CancellationToken ct)
    {
        var allowTasks = occurrenceOnly ? flight.CanSaveReturnsToRamp : flight.CanSaveTasks;
        var allowServices = occurrenceOnly ? flight.CanSaveReturnsToRamp : flight.CanSaveServices;
        var unavailable = false;

        var aircraft = Read(!occurrenceOnly && flight.CanSaveArrival, async () =>
            (await api.GetAircraftTypeOptionsAsync(ct)).Select(item => new PortalCatalogChoice(item.Id, $"{item.Manufacturer} · {item.Model}")));
        var services = Read(allowServices, async () =>
            (await api.GetPerformedServiceOptionsAsync(ct)).Where(item => administratorEntry || flight.AllowedServiceIds?.Contains(item.Id) == true)
                .Select(item => new PortalCatalogChoice(item.Id, item.Name)));
        var staff = Read(allowTasks || allowServices || administratorEntry && (flight.CanSaveArrival || flight.CanSaveDeparture), async () =>
            (await api.GetStaffMemberOptionsAsync(flight.StationId, ct))
                .Select(item => new PortalCatalogChoice(item.Id, $"{item.FullName} · {item.EmployeeId}")
                {
                    FullName = item.FullName,
                    ManpowerTypeName = item.ManpowerTypeName
                }));
        var chapters = Read(allowTasks, async () =>
            (await api.GetAtaChapterOptionsAsync(ct)).Select(item => new PortalCatalogChoice(item.Id, $"{item.Code} · {item.Title}")));
        var tools = Read(allowTasks, async () =>
            (await api.GetToolOptionsAsync(ct)).Select(item => new PortalCatalogChoice(item.Id, item.Name, item.CalculationType)));
        var materials = Read(allowTasks, async () =>
            (await api.GetMaterialOptionsAsync(ct)).Select(item => new PortalCatalogChoice(item.Id, item.Name, item.CalculationType)));
        var supports = Read(allowTasks, async () =>
            (await api.GetGeneralSupportOptionsAsync(ct)).Select(item => new PortalCatalogChoice(item.Id, item.Name, item.CalculationType)));

        await Task.WhenAll(aircraft, services, staff, chapters, tools, materials, supports);
        return new(await aircraft, await services, await staff, await chapters, await tools, await materials, await supports, unavailable);

        async Task<IReadOnlyList<PortalCatalogChoice>> Read(bool required, Func<Task<IEnumerable<PortalCatalogChoice>>> read)
        {
            if (!required)
                return [];

            try
            {
                return (await read()).ToArray();
            }
            catch (ApiException ex) when (ex.IsForbidden)
            {
                unavailable = true;
                return [];
            }
        }
    }

    public static IEnumerable<PortalCatalogChoice> WithSaved(
        IReadOnlyList<PortalCatalogChoice> options, Guid? id, string? label, ResourceCalculationType? calculation = null) =>
        id is { } selected && options.All(option => option.Id != selected)
            ? options.Prepend(new PortalCatalogChoice(selected, label ?? "Saved selection", calculation))
            : options;
}

public sealed class PortalSignatureDraft(string? savedPath = null)
{
    public string? SavedPath { get; } = savedPath;
    public string? FileName { get; set; }
    public byte[]? Bytes { get; set; }
    public bool IsReading { get; set; }
    public bool RemoveSaved { get; set; }

    public void Clear()
    {
        Bytes = null;
        FileName = null;
    }
}
