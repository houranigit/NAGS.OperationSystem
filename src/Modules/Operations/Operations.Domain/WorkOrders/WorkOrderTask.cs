using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

/// <summary>A Major/Minor work activity on a work order with participating employees, consumed resources, time, and attachments.</summary>
public sealed class WorkOrderTask : Entity<Guid>
{
    private readonly List<WorkOrderTaskEmployee> _employees = [];
    private readonly List<WorkOrderTaskTool> _tools = [];
    private readonly List<WorkOrderTaskMaterial> _materials = [];
    private readonly List<WorkOrderTaskGeneralSupport> _generalSupports = [];
    private readonly List<WorkOrderTaskAttachment> _attachments = [];

    private WorkOrderTask() { }

    private WorkOrderTask(Guid id, Guid workOrderId, TaskType taskType, string? description, TimeWindow window, bool returnToRamp)
    {
        Id = id;
        WorkOrderId = workOrderId;
        TaskType = taskType;
        Description = description;
        Window = window;
        ReturnToRamp = returnToRamp;
    }

    public Guid WorkOrderId { get; private set; }
    public TaskType TaskType { get; private set; }
    public string? Description { get; private set; }
    public TimeWindow Window { get; private set; } = null!;
    public bool ReturnToRamp { get; private set; }

    public IReadOnlyList<WorkOrderTaskEmployee> Employees => _employees.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskTool> Tools => _tools.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskMaterial> Materials => _materials.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskGeneralSupport> GeneralSupports => _generalSupports.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskAttachment> Attachments => _attachments.AsReadOnly();

    internal static Result<WorkOrderTask> Create(Guid workOrderId, TaskInput input)
    {
        var window = TimeWindow.Create(input.From, input.To);
        if (window.IsFailure)
            return window.Error;

        if (input.Employees.Count == 0)
            return Error.Validation("A task must have at least one employee.", "Operations.Task.EmployeeRequired");

        if (!string.IsNullOrWhiteSpace(input.Description) && input.Description.Trim().Length > 1000)
            return Error.Validation("Task description must be at most 1000 characters.", "Operations.Task.DescriptionTooLong");

        var task = new WorkOrderTask(
            Guid.NewGuid(), workOrderId, input.TaskType, string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            window.Value, input.ReturnToRamp);

        foreach (var employee in input.Employees.GroupBy(e => e.StaffMemberId).Select(g => g.First()))
            task._employees.Add(new WorkOrderTaskEmployee(Guid.NewGuid(), task.Id, employee));

        foreach (var tool in input.Tools)
        {
            var qty = Quantity.Create(tool.Quantity);
            if (qty.IsFailure)
                return qty.Error;
            task._tools.Add(new WorkOrderTaskTool(Guid.NewGuid(), task.Id, tool.Tool, qty.Value));
        }

        foreach (var material in input.Materials)
        {
            var qty = Quantity.Create(material.Quantity);
            if (qty.IsFailure)
                return qty.Error;
            task._materials.Add(new WorkOrderTaskMaterial(Guid.NewGuid(), task.Id, material.Material, qty.Value));
        }

        foreach (var gs in input.GeneralSupports)
        {
            var qty = Quantity.Create(gs.Quantity);
            if (qty.IsFailure)
                return qty.Error;
            task._generalSupports.Add(new WorkOrderTaskGeneralSupport(Guid.NewGuid(), task.Id, gs.GeneralSupport, qty.Value));
        }

        foreach (var attachment in input.Attachments)
        {
            var created = WorkOrderTaskAttachment.Create(task.Id, attachment);
            if (created.IsFailure)
                return created.Error;
            task._attachments.Add(created.Value);
        }

        return task;
    }
}

public sealed class WorkOrderTaskEmployee : Entity<Guid>
{
    private WorkOrderTaskEmployee() { }

    internal WorkOrderTaskEmployee(Guid id, Guid taskId, StaffMemberSnapshot employee)
    {
        Id = id;
        TaskId = taskId;
        Employee = employee;
    }

    public Guid TaskId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
}

public sealed class WorkOrderTaskTool : Entity<Guid>
{
    private WorkOrderTaskTool() { }

    internal WorkOrderTaskTool(Guid id, Guid taskId, ToolSnapshot tool, Quantity quantity)
    {
        Id = id;
        TaskId = taskId;
        Tool = tool;
        Quantity = quantity;
    }

    public Guid TaskId { get; private set; }
    public ToolSnapshot Tool { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskMaterial : Entity<Guid>
{
    private WorkOrderTaskMaterial() { }

    internal WorkOrderTaskMaterial(Guid id, Guid taskId, MaterialSnapshot material, Quantity quantity)
    {
        Id = id;
        TaskId = taskId;
        Material = material;
        Quantity = quantity;
    }

    public Guid TaskId { get; private set; }
    public MaterialSnapshot Material { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskGeneralSupport : Entity<Guid>
{
    private WorkOrderTaskGeneralSupport() { }

    internal WorkOrderTaskGeneralSupport(Guid id, Guid taskId, GeneralSupportSnapshot generalSupport, Quantity quantity)
    {
        Id = id;
        TaskId = taskId;
        GeneralSupport = generalSupport;
        Quantity = quantity;
    }

    public Guid TaskId { get; private set; }
    public GeneralSupportSnapshot GeneralSupport { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskAttachment : Entity<Guid>
{
    private WorkOrderTaskAttachment() { }

    private WorkOrderTaskAttachment(Guid id, Guid taskId, TaskAttachmentKind kind, string contentType, string fileName, long sizeBytes, string storageReference, DateTimeOffset capturedAtUtc)
    {
        Id = id;
        TaskId = taskId;
        Kind = kind;
        ContentType = contentType;
        FileName = fileName;
        SizeBytes = sizeBytes;
        StorageReference = storageReference;
        CapturedAtUtc = capturedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public TaskAttachmentKind Kind { get; private set; }
    public string ContentType { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    /// <summary>Reference to the stored file in object/file storage (bytes are never stored in the database).</summary>
    public string StorageReference { get; private set; } = null!;
    public DateTimeOffset CapturedAtUtc { get; private set; }

    internal static Result<WorkOrderTaskAttachment> Create(Guid taskId, AttachmentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.StorageReference))
            return Error.Validation("Attachment storage reference is required.", "Operations.Attachment.StorageRequired");
        if (string.IsNullOrWhiteSpace(input.FileName))
            return Error.Validation("Attachment file name is required.", "Operations.Attachment.FileNameRequired");
        if (input.SizeBytes <= 0)
            return Error.Validation("Attachment size must be greater than zero.", "Operations.Attachment.SizeInvalid");

        return new WorkOrderTaskAttachment(
            Guid.NewGuid(), taskId, input.Kind, input.ContentType.Trim(), input.FileName.Trim(),
            input.SizeBytes, input.StorageReference.Trim(), input.CapturedAtUtc.ToUniversalTime());
    }
}

// --- Validated inputs -------------------------------------------------------

public sealed record TaskInput(
    TaskType TaskType,
    string? Description,
    DateTimeOffset From,
    DateTimeOffset To,
    bool ReturnToRamp,
    IReadOnlyList<StaffMemberSnapshot> Employees,
    IReadOnlyList<ToolUsageInput> Tools,
    IReadOnlyList<MaterialUsageInput> Materials,
    IReadOnlyList<GeneralSupportUsageInput> GeneralSupports,
    IReadOnlyList<AttachmentInput> Attachments);

public sealed record ToolUsageInput(ToolSnapshot Tool, decimal Quantity);

public sealed record MaterialUsageInput(MaterialSnapshot Material, decimal Quantity);

public sealed record GeneralSupportUsageInput(GeneralSupportSnapshot GeneralSupport, decimal Quantity);

public sealed record AttachmentInput(
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    long SizeBytes,
    string StorageReference,
    DateTimeOffset CapturedAtUtc);
