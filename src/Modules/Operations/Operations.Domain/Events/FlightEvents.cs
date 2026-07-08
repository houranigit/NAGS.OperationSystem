using BuildingBlocks.Domain.Events;

namespace Operations.Domain.Events;

public sealed record FlightScheduled(Guid FlightId) : DomainEvent;

public sealed record FlightNumberChanged(Guid FlightId, string OriginalFlightNumber, string NewFlightNumber) : DomainEvent;

public sealed record EmployeeAssignedToFlight(Guid FlightId, Guid StaffMemberId) : DomainEvent;

public sealed record FlightMerged(Guid LoserFlightId, Guid SurvivorFlightId) : DomainEvent;
