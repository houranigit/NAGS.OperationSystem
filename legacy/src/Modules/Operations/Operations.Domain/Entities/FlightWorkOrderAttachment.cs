using BuildingBlocks.Domain.Entities;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Entities;

public sealed class FlightWorkOrderAttachment : Entity<Guid>
{
    public FlightId FlightId { get; private set; } = null!;
    public WorkOrderId WorkOrderId { get; private set; } = null!;

    private FlightWorkOrderAttachment()
    {
    }

    internal FlightWorkOrderAttachment(Guid id, FlightId flightId, WorkOrderId workOrderId)
    {
        Id = id;
        FlightId = flightId;
        WorkOrderId = workOrderId;
    }
}
