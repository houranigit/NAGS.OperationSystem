using BuildingBlocks.Domain.Events;

namespace Operations.Domain.Events;

public sealed record FlightScheduled(Guid FlightId) : DomainEvent;

public sealed record AdHocFlightCreated(Guid FlightId) : DomainEvent;

public sealed record FlightNumberChanged(Guid FlightId, string OriginalFlightNumber, string NewFlightNumber) : DomainEvent;

public sealed record EmployeeAssignedToFlight(Guid FlightId, Guid StaffMemberId) : DomainEvent;

public sealed record FlightCompleted(Guid FlightId, Guid WorkOrderId) : DomainEvent;

public sealed record FlightCanceled(Guid FlightId, Guid WorkOrderId) : DomainEvent;

public sealed record FlightMerged(Guid LoserFlightId, Guid SurvivorFlightId) : DomainEvent;

/// <summary>Raised when a returned/reverted approval clears the captured work order values from the flight.</summary>
public sealed record ApprovedWorkOrderSnapshotCleared(Guid FlightId, Guid WorkOrderId) : DomainEvent;
