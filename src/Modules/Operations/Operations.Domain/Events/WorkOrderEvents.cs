using BuildingBlocks.Domain.Events;

namespace Operations.Domain.Events;

public sealed record WorkOrderOpened(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderSubmitted(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderApproved(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderReturnedToReview(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderRejected(Guid WorkOrderId, Guid FlightId) : DomainEvent;

public sealed record WorkOrderSuperseded(Guid WorkOrderId, Guid SurvivorWorkOrderId) : DomainEvent;
