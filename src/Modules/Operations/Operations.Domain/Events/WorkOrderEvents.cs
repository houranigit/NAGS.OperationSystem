using BuildingBlocks.Domain.Events;

namespace Operations.Domain.Events;

public sealed record WorkOrderSubmitted(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderUpdated(Guid WorkOrderId) : DomainEvent;

public sealed record WorkOrderConverted(Guid WorkOrderId) : DomainEvent;

public sealed record WorkOrderApproved(Guid WorkOrderId, string ApprovalNumber) : DomainEvent;

public sealed record WorkOrderReturned(Guid WorkOrderId) : DomainEvent;

public sealed record WorkOrderMerged(Guid WorkOrderId, Guid GeneratedWorkOrderId) : DomainEvent;
