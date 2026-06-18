using BuildingBlocks.Domain.Entities;
using Store.Contracts.Features.Material;

namespace Operations.Domain.Entities;

/// <summary>A material consumed by a <see cref="WorkOrderTask"/>.</summary>
public sealed class WorkOrderTaskMaterial : Entity<Guid>
{
    public Guid TaskId { get; private set; }
    public MaterialSnapshot Material { get; private set; } = null!;

    private WorkOrderTaskMaterial()
    {
    }

    internal WorkOrderTaskMaterial(Guid id, Guid taskId, MaterialSnapshot material)
    {
        Id = id;
        TaskId = taskId;
        Material = material;
    }
}
