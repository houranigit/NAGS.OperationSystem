using BuildingBlocks.Domain.Entities;
using Store.Contracts.Features.GeneralSupport;

namespace Operations.Domain.Entities;

/// <summary>A general-support item consumed by a <see cref="WorkOrderTask"/>.</summary>
public sealed class WorkOrderTaskGeneralSupport : Entity<Guid>
{
    public Guid TaskId { get; private set; }
    public GeneralSupportSnapshot GeneralSupport { get; private set; } = null!;

    private WorkOrderTaskGeneralSupport()
    {
    }

    internal WorkOrderTaskGeneralSupport(Guid id, Guid taskId, GeneralSupportSnapshot generalSupport)
    {
        Id = id;
        TaskId = taskId;
        GeneralSupport = generalSupport;
    }
}
