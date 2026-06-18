using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Employee;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Operations.Domain.Entities;

/// <summary>
/// Single unit of work performed on a <see cref="WorkOrder"/>. Replaces the legacy
/// <c>WorkOrderEmployeeLine</c> + <c>CorrectiveAction</c> pair and folds in store usage
/// (tools / materials / general supports) plus rich attachments. Each task captures a
/// description, a time window, the participating employees, optional store items, and any
/// number of attachments. The <see cref="ReturnToRamp"/> flag lives on the task itself so a
/// single work order can mix RTR and non-RTR tasks.
/// </summary>
public sealed class WorkOrderTask : Entity<Guid>
{
    public WorkOrderId WorkOrderId { get; private set; } = null!;
    public TaskType TaskType { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public bool ReturnToRamp { get; private set; }

    private readonly List<WorkOrderTaskEmployee> _employees = [];
    public IReadOnlyList<WorkOrderTaskEmployee> Employees => _employees;

    private readonly List<WorkOrderTaskTool> _tools = [];
    public IReadOnlyList<WorkOrderTaskTool> Tools => _tools;

    private readonly List<WorkOrderTaskMaterial> _materials = [];
    public IReadOnlyList<WorkOrderTaskMaterial> Materials => _materials;

    private readonly List<WorkOrderTaskGeneralSupport> _generalSupports = [];
    public IReadOnlyList<WorkOrderTaskGeneralSupport> GeneralSupports => _generalSupports;

    private readonly List<WorkOrderTaskAttachment> _attachments = [];
    public IReadOnlyList<WorkOrderTaskAttachment> Attachments => _attachments;

    private WorkOrderTask()
    {
    }

    internal static Result<WorkOrderTask> Create(
        WorkOrderId workOrderId,
        TaskType taskType,
        string? description,
        DateTimeOffset from,
        DateTimeOffset to,
        bool returnToRamp,
        IReadOnlyList<EmployeeSnapshot>? employees,
        IReadOnlyList<ToolSnapshot>? tools,
        IReadOnlyList<MaterialSnapshot>? materials,
        IReadOnlyList<GeneralSupportSnapshot>? generalSupports,
        IReadOnlyList<TaskAttachmentInput>? attachments)
    {
        if (to < from)
            return Error.Validation("Task end time must be on or after start time.");

        if (description is { Length: > 2000 })
            return Error.Validation("Task description must not exceed 2000 characters.");

        var id = Guid.NewGuid();
        var task = new WorkOrderTask
        {
            Id = id,
            WorkOrderId = workOrderId,
            TaskType = taskType,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            From = from,
            To = to,
            ReturnToRamp = returnToRamp
        };

        if (employees is not null)
        {
            var seen = new HashSet<Guid>();
            foreach (var e in employees)
            {
                if (!seen.Add(e.EmployeeId))
                    return Error.Validation($"Employee '{e.FullName}' is listed more than once on a task.");
                task._employees.Add(new WorkOrderTaskEmployee(Guid.NewGuid(), id, e));
            }
        }

        if (tools is not null)
            foreach (var t in tools)
                task._tools.Add(new WorkOrderTaskTool(Guid.NewGuid(), id, t));

        if (materials is not null)
            foreach (var m in materials)
                task._materials.Add(new WorkOrderTaskMaterial(Guid.NewGuid(), id, m));

        if (generalSupports is not null)
            foreach (var g in generalSupports)
                task._generalSupports.Add(new WorkOrderTaskGeneralSupport(Guid.NewGuid(), id, g));

        if (attachments is not null)
        {
            foreach (var a in attachments)
            {
                var built = WorkOrderTaskAttachment.Create(id, a.Kind, a.ContentType, a.FileName, a.Bytes, a.CapturedAt);
                if (built.IsFailure) return built.Error;
                task._attachments.Add(built.Value);
            }
        }

        return task;
    }
}

/// <summary>
/// Transport-only payload for a task attachment as supplied by the application layer.
/// Validation lives in <see cref="WorkOrderTaskAttachment.Create"/>.
/// </summary>
public readonly record struct TaskAttachmentInput(
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    byte[] Bytes,
    DateTimeOffset CapturedAt);
