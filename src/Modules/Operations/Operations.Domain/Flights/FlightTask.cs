using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>Actual task work and its individual resource and employee periods.</summary>
public sealed class FlightTask : Entity<Guid>
{
    public const int MaxAttachments = 10;

    private readonly List<FlightTaskTool> _tools = [];
    private readonly List<FlightTaskMaterial> _materials = [];
    private readonly List<FlightTaskGeneralSupport> _generalSupports = [];
    private readonly List<FlightTaskPerformer> _performers = [];
    private readonly List<FlightTaskAttachment> _attachments = [];

    private FlightTask() { }

    public Guid FlightId { get; private set; }
    public Guid? ReturnToRampId { get; private set; }
    public FlightTaskType TaskType { get; private set; }
    public AtaChapterSnapshot? AtaChapter { get; private set; }
    public string? CustomerReferenceNumber { get; private set; }
    public TimeWindow Window { get; private set; } = null!;
    public Guid? EndedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<FlightTaskTool> Tools => _tools.AsReadOnly();
    public IReadOnlyList<FlightTaskMaterial> Materials => _materials.AsReadOnly();
    public IReadOnlyList<FlightTaskGeneralSupport> GeneralSupports => _generalSupports.AsReadOnly();
    public IReadOnlyList<FlightTaskPerformer> Performers => _performers.AsReadOnly();
    public IReadOnlyList<FlightTaskAttachment> Attachments => _attachments.AsReadOnly();

    internal static Result<FlightTask> Create(Guid flightId, FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var check = ValidateDetails(taskType, ataChapter, customerReferenceNumber, window, notes);
        if (check.IsFailure)
            return check.Error;

        return new FlightTask
        {
            Id = Guid.NewGuid(),
            FlightId = flightId,
            ReturnToRampId = returnToRampId,
            TaskType = taskType,
            AtaChapter = CopyAtaChapter(ataChapter),
            CustomerReferenceNumber = NormalizeOptional(customerReferenceNumber),
            Window = window!.Copy(),
            EndedByUserId = window.ToUtc.HasValue ? userId : null,
            Notes = NormalizeOptional(notes),
            CreatedByUserId = userId,
            CreatedAtUtc = now
        };
    }

    internal Result<bool> UpdateDetails(FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId)
    {
        var check = ValidateDetails(taskType, ataChapter, customerReferenceNumber, window, notes);
        if (check.IsFailure)
            return check.Error;

        foreach (var item in _tools)
        {
            var period = OperationalWorkValidation.ResourceWindow(item.Usage, window!, "Tool");
            if (period.IsFailure)
                return period.Error;
        }
        foreach (var item in _materials)
        {
            var period = OperationalWorkValidation.ResourceWindow(item.Usage, window!, "Material");
            if (period.IsFailure)
                return period.Error;
        }
        foreach (var item in _generalSupports)
        {
            var period = OperationalWorkValidation.ResourceWindow(item.Usage, window!, "GeneralSupport");
            if (period.IsFailure)
                return period.Error;
        }
        foreach (var performer in _performers)
        {
            var period = OperationalWorkValidation.EmployeeWindow(performer.Window, window!);
            if (period.IsFailure)
                return period.Error;
        }

        var reference = NormalizeOptional(customerReferenceNumber);
        var normalizedNotes = NormalizeOptional(notes);
        if (TaskType == taskType && AtaChapter == ataChapter && Window == window
            && CustomerReferenceNumber == reference && Notes == normalizedNotes)
            return false;

        if (Window.ToUtc != window!.ToUtc)
            EndedByUserId = window.ToUtc.HasValue ? userId : null;

        TaskType = taskType;
        AtaChapter = CopyAtaChapter(ataChapter);
        CustomerReferenceNumber = reference;
        Window = window.Copy();
        Notes = normalizedNotes;
        return true;
    }

    internal Result<FlightTaskTool> AddTool(ToolSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var check = ValidateTool(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_tools.Any(record => record.Tool.ToolId == item!.ToolId))
            return Error.Validation("Duplicate tool rows are not allowed within one task.", "Operations.ResourceUsage.ToolDuplicate");

        var record = new FlightTaskTool(Guid.NewGuid(), Id, item!, usage!, notes);
        _tools.Add(record);
        return record;
    }

    internal Result<bool> UpdateTool(Guid recordId, ToolSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var record = _tools.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task tool record was not found.", "Operations.FlightTask.ToolNotFound");
        var check = ValidateTool(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_tools.Any(entry => entry.Id != recordId && entry.Tool.ToolId == item!.ToolId))
            return Error.Validation("Duplicate tool rows are not allowed within one task.", "Operations.ResourceUsage.ToolDuplicate");
        if (record.Tool == item && record.Usage == usage && record.Notes == NormalizeOptional(notes))
            return false;

        record.Update(item!, usage!, notes);
        return true;
    }

    internal Result RemoveTool(Guid recordId)
    {
        var record = _tools.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task tool record was not found.", "Operations.FlightTask.ToolNotFound");
        _tools.Remove(record);
        return Result.Success();
    }

    private Result ValidateTool(ToolSnapshot? item, ResourceUsage? usage, string? notes)
    {
        if (item is null || item.ToolId == Guid.Empty)
            return Error.Validation("Tool is required.", "Operations.FlightTask.ToolRequired");
        if (usage is null)
            return Error.Validation("Resource usage is required.", "Operations.FlightTask.UsageRequired");
        if (item.CalculationType != usage.CalculationType)
            return Error.Validation("Resource usage does not match its calculation type.", "Operations.ResourceUsage.ToolCalculationTypeMismatch");
        var description = OperationalWorkValidation.Description(item.ToolId, WellKnownMasterDataIds.UnknownTool, notes, "Tool");
        if (description.IsFailure)
            return description;
        return OperationalWorkValidation.ResourceWindow(usage, Window, "Tool");
    }

    internal Result<FlightTaskMaterial> AddMaterial(MaterialSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var check = ValidateMaterial(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_materials.Any(record => record.Material.MaterialId == item!.MaterialId))
            return Error.Validation("Duplicate material rows are not allowed within one task.", "Operations.ResourceUsage.MaterialDuplicate");

        var record = new FlightTaskMaterial(Guid.NewGuid(), Id, item!, usage!, notes);
        _materials.Add(record);
        return record;
    }

    internal Result<bool> UpdateMaterial(Guid recordId, MaterialSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var record = _materials.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task material record was not found.", "Operations.FlightTask.MaterialNotFound");
        var check = ValidateMaterial(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_materials.Any(entry => entry.Id != recordId && entry.Material.MaterialId == item!.MaterialId))
            return Error.Validation("Duplicate material rows are not allowed within one task.", "Operations.ResourceUsage.MaterialDuplicate");
        if (record.Material == item && record.Usage == usage && record.Notes == NormalizeOptional(notes))
            return false;

        record.Update(item!, usage!, notes);
        return true;
    }

    internal Result RemoveMaterial(Guid recordId)
    {
        var record = _materials.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task material record was not found.", "Operations.FlightTask.MaterialNotFound");
        _materials.Remove(record);
        return Result.Success();
    }

    private Result ValidateMaterial(MaterialSnapshot? item, ResourceUsage? usage, string? notes)
    {
        if (item is null || item.MaterialId == Guid.Empty)
            return Error.Validation("Material is required.", "Operations.FlightTask.MaterialRequired");
        if (usage is null)
            return Error.Validation("Resource usage is required.", "Operations.FlightTask.UsageRequired");
        if (item.CalculationType != usage.CalculationType)
            return Error.Validation("Resource usage does not match its calculation type.", "Operations.ResourceUsage.MaterialCalculationTypeMismatch");
        var description = OperationalWorkValidation.Description(item.MaterialId, WellKnownMasterDataIds.UnknownMaterial, notes, "Material");
        if (description.IsFailure)
            return description;
        return OperationalWorkValidation.ResourceWindow(usage, Window, "Material");
    }

    internal Result<FlightTaskGeneralSupport> AddGeneralSupport(GeneralSupportSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var check = ValidateGeneralSupport(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_generalSupports.Any(record => record.GeneralSupport.GeneralSupportId == item!.GeneralSupportId))
            return Error.Validation("Duplicate general support rows are not allowed within one task.", "Operations.ResourceUsage.GeneralSupportDuplicate");

        var record = new FlightTaskGeneralSupport(Guid.NewGuid(), Id, item!, usage!, notes);
        _generalSupports.Add(record);
        return record;
    }

    internal Result<bool> UpdateGeneralSupport(Guid recordId, GeneralSupportSnapshot? item, ResourceUsage? usage, string? notes)
    {
        var record = _generalSupports.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task general support record was not found.", "Operations.FlightTask.GeneralSupportNotFound");
        var check = ValidateGeneralSupport(item, usage, notes);
        if (check.IsFailure)
            return check.Error;
        if (_generalSupports.Any(entry => entry.Id != recordId && entry.GeneralSupport.GeneralSupportId == item!.GeneralSupportId))
            return Error.Validation("Duplicate general support rows are not allowed within one task.", "Operations.ResourceUsage.GeneralSupportDuplicate");
        if (record.GeneralSupport == item && record.Usage == usage && record.Notes == NormalizeOptional(notes))
            return false;

        record.Update(item!, usage!, notes);
        return true;
    }

    internal Result RemoveGeneralSupport(Guid recordId)
    {
        var record = _generalSupports.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task general support record was not found.", "Operations.FlightTask.GeneralSupportNotFound");
        _generalSupports.Remove(record);
        return Result.Success();
    }

    private Result ValidateGeneralSupport(GeneralSupportSnapshot? item, ResourceUsage? usage, string? notes)
    {
        if (item is null || item.GeneralSupportId == Guid.Empty)
            return Error.Validation("General support is required.", "Operations.FlightTask.GeneralSupportRequired");
        if (usage is null)
            return Error.Validation("Resource usage is required.", "Operations.FlightTask.UsageRequired");
        if (item.CalculationType != usage.CalculationType)
            return Error.Validation("Resource usage does not match its calculation type.", "Operations.ResourceUsage.GeneralSupportCalculationTypeMismatch");
        var description = OperationalWorkValidation.Description(item.GeneralSupportId, WellKnownMasterDataIds.UnknownGeneralSupport, notes, "General support");
        if (description.IsFailure)
            return description;
        return OperationalWorkValidation.ResourceWindow(usage, Window, "GeneralSupport");
    }

    internal Result<FlightTaskPerformer> AddPerformer(StaffMemberSnapshot? item, TimeWindow? window, string? notes)
    {
        var check = ValidatePerformer(item, window, notes);
        if (check.IsFailure)
            return check.Error;

        if (_performers.Any(record => record.Employee.StaffMemberId == item!.StaffMemberId))
            return OperationalWorkValidation.DuplicateEmployee();

        var record = new FlightTaskPerformer(Guid.NewGuid(), Id, item!, window!, notes);
        _performers.Add(record);
        return record;
    }

    internal Result<bool> UpdatePerformer(Guid recordId, StaffMemberSnapshot? item, TimeWindow? window, string? notes)
    {
        var record = _performers.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task performing employee record was not found.", "Operations.FlightTask.PerformerNotFound");
        var check = ValidatePerformer(item, window, notes);
        if (check.IsFailure)
            return check.Error;
        if (_performers.Any(entry => entry.Id != recordId && entry.Employee.StaffMemberId == item!.StaffMemberId))
            return OperationalWorkValidation.DuplicateEmployee();
        if (record.Employee == item && record.Window == window && record.Notes == NormalizeOptional(notes))
            return false;

        record.Update(item!, window!, notes);
        return true;
    }

    internal Result RemovePerformer(Guid recordId)
    {
        var record = _performers.SingleOrDefault(entry => entry.Id == recordId);
        if (record is null)
            return Error.NotFound("Task performing employee record was not found.", "Operations.FlightTask.PerformerNotFound");
        _performers.Remove(record);
        return Result.Success();
    }

    private Result ValidatePerformer(StaffMemberSnapshot? item, TimeWindow? window, string? notes)
    {
        if (item is null || item.StaffMemberId == Guid.Empty)
            return Error.Validation("Performing employee is required.", "Operations.FlightTask.EmployeeRequired");
        if (window is null)
            return Error.Validation("Task work time window is required.", "Operations.FlightTask.WindowRequired");
        var description = OperationalWorkValidation.Notes(notes);
        if (description.IsFailure)
            return description;
        return OperationalWorkValidation.EmployeeWindow(window, Window);
    }

    internal Result<FlightTaskAttachment> AddAttachment(AttachmentDetails? details, Guid userId, DateTimeOffset now)
    {
        if (details is null)
            return Error.Validation("Attachment details are required.", "Operations.FlightTask.AttachmentRequired");

        if (_attachments.Count >= MaxAttachments)
            return Error.Validation("A task can have at most 10 attachments.", "Operations.WorkOrder.AttachmentLimitExceeded");

        var attachment = new FlightTaskAttachment(Guid.NewGuid(), Id, details, userId, now);
        _attachments.Add(attachment);
        return attachment;
    }

    internal Result<FlightTaskAttachment> RemoveAttachment(Guid attachmentId)
    {
        var attachment = _attachments.SingleOrDefault(item => item.Id == attachmentId);
        if (attachment is null)
            return Error.NotFound("Task attachment was not found.", "Operations.FlightTask.AttachmentNotFound");
        _attachments.Remove(attachment);
        return attachment;
    }

    internal void MarkUpdated(Guid userId, DateTimeOffset now)
    {
        UpdatedByUserId = userId;
        UpdatedAtUtc = now;
    }

    private static Result ValidateDetails(FlightTaskType taskType, AtaChapterSnapshot? ataChapter, string? customerReferenceNumber,
        TimeWindow? window, string? notes)
    {
        if (taskType is not (FlightTaskType.Major or FlightTaskType.Minor))
            return Error.Validation("Task type must be Major or Minor.", "Operations.FlightTask.TaskTypeInvalid");
        if (ataChapter is not null && ataChapter.AtaChapterId == Guid.Empty)
            return Error.Validation("ATA chapter is invalid.", "Operations.FlightTask.AtaChapterInvalid");
        if (window is null)
            return Error.Validation("Task time window is required.", "Operations.FlightTask.WindowRequired");
        var reference = OperationalWorkValidation.ReferenceNumber(customerReferenceNumber);
        if (reference.IsFailure)
            return reference;
        return OperationalWorkValidation.Notes(notes);
    }

    private static AtaChapterSnapshot? CopyAtaChapter(AtaChapterSnapshot? chapter) =>
        chapter is null ? null : new AtaChapterSnapshot(chapter.AtaChapterId, chapter.Code, chapter.Title);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
