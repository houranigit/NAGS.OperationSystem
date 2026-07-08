using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>Raised when a staff member is assigned to a flight (future Notifications consumer).</summary>
public sealed record FlightEmployeeAssigned : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required Guid StaffMemberId { get; init; }
}
