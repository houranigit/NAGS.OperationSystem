using BuildingBlocks.Domain.Entities;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrderTask : Entity<Guid>
{
    private readonly List<WorkOrderTaskEmployee> _employees = [];
    private readonly List<WorkOrderTaskTool> _tools = [];
    private readonly List<WorkOrderTaskMaterial> _materials = [];
    private readonly List<WorkOrderTaskGeneralSupport> _generalSupports = [];
    private readonly List<WorkOrderTaskAttachment> _attachments = [];

    private WorkOrderTask() { }

    internal WorkOrderTask(Guid id, Guid workOrderId, WorkOrderTaskInput input)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Apply(input);
    }

    public Guid WorkOrderId { get; private set; }
    public TaskType TaskType { get; private set; }
    public string? Description { get; private set; }
    public TimeWindow Window { get; private set; } = null!;

    public IReadOnlyList<WorkOrderTaskEmployee> Employees => _employees.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskTool> Tools => _tools.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskMaterial> Materials => _materials.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskGeneralSupport> GeneralSupports => _generalSupports.AsReadOnly();
    public IReadOnlyList<WorkOrderTaskAttachment> Attachments => _attachments.AsReadOnly();

    internal void Update(WorkOrderTaskInput input) => Apply(input);

    private void Apply(WorkOrderTaskInput input)
    {
        TaskType = input.TaskType;
        Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        Window = input.Window;

        _employees.Clear();
        foreach (var employee in input.Employees.GroupBy(e => e.StaffMemberId).Select(g => g.First()))
            _employees.Add(new WorkOrderTaskEmployee(Guid.NewGuid(), WorkOrderId, Id, employee));

        _tools.Clear();
        foreach (var item in input.Tools.GroupBy(t => t.Tool.ToolId).Select(g => g.First()))
            _tools.Add(new WorkOrderTaskTool(Guid.NewGuid(), WorkOrderId, Id, item.Tool, item.Quantity));

        _materials.Clear();
        foreach (var item in input.Materials.GroupBy(m => m.Material.MaterialId).Select(g => g.First()))
            _materials.Add(new WorkOrderTaskMaterial(Guid.NewGuid(), WorkOrderId, Id, item.Material, item.Quantity));

        _generalSupports.Clear();
        foreach (var item in input.GeneralSupports.GroupBy(g => g.GeneralSupport.GeneralSupportId).Select(g => g.First()))
            _generalSupports.Add(new WorkOrderTaskGeneralSupport(Guid.NewGuid(), WorkOrderId, Id, item.GeneralSupport, item.Quantity));
    }
}

public sealed class WorkOrderTaskEmployee : Entity<Guid>
{
    private WorkOrderTaskEmployee() { }

    internal WorkOrderTaskEmployee(Guid id, Guid workOrderId, Guid workOrderTaskId, StaffMemberSnapshot employee)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderTaskId = workOrderTaskId;
        Employee = employee;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderTaskId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
}

public sealed class WorkOrderTaskTool : Entity<Guid>
{
    private WorkOrderTaskTool() { }

    internal WorkOrderTaskTool(Guid id, Guid workOrderId, Guid workOrderTaskId, ToolSnapshot tool, Quantity quantity)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderTaskId = workOrderTaskId;
        Tool = tool;
        Quantity = quantity;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderTaskId { get; private set; }
    public ToolSnapshot Tool { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskMaterial : Entity<Guid>
{
    private WorkOrderTaskMaterial() { }

    internal WorkOrderTaskMaterial(Guid id, Guid workOrderId, Guid workOrderTaskId, MaterialSnapshot material, Quantity quantity)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderTaskId = workOrderTaskId;
        Material = material;
        Quantity = quantity;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderTaskId { get; private set; }
    public MaterialSnapshot Material { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskGeneralSupport : Entity<Guid>
{
    private WorkOrderTaskGeneralSupport() { }

    internal WorkOrderTaskGeneralSupport(Guid id, Guid workOrderId, Guid workOrderTaskId, GeneralSupportSnapshot generalSupport, Quantity quantity)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderTaskId = workOrderTaskId;
        GeneralSupport = generalSupport;
        Quantity = quantity;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderTaskId { get; private set; }
    public GeneralSupportSnapshot GeneralSupport { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
}

public sealed class WorkOrderTaskAttachment : Entity<Guid>
{
    private WorkOrderTaskAttachment() { }

    public WorkOrderTaskAttachment(
        Guid workOrderId,
        Guid workOrderTaskId,
        TaskAttachmentKind kind,
        string storageReference,
        string originalFileName,
        string contentType,
        long size)
    {
        Id = Guid.NewGuid();
        WorkOrderId = workOrderId;
        WorkOrderTaskId = workOrderTaskId;
        Kind = kind;
        StorageReference = storageReference;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        Size = size;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderTaskId { get; private set; }
    public TaskAttachmentKind Kind { get; private set; }
    public string StorageReference { get; private set; } = null!;
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long Size { get; private set; }
}
