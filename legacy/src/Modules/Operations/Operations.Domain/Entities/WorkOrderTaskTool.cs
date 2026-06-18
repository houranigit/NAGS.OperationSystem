using BuildingBlocks.Domain.Entities;
using Store.Contracts.Features.Tool;

namespace Operations.Domain.Entities;

/// <summary>A tool consumed by a <see cref="WorkOrderTask"/>.</summary>
public sealed class WorkOrderTaskTool : Entity<Guid>
{
    public Guid TaskId { get; private set; }
    public ToolSnapshot Tool { get; private set; } = null!;

    private WorkOrderTaskTool()
    {
    }

    internal WorkOrderTaskTool(Guid id, Guid taskId, ToolSnapshot tool)
    {
        Id = id;
        TaskId = taskId;
        Tool = tool;
    }
}
