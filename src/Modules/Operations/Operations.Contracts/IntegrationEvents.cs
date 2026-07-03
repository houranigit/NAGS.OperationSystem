using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>
/// Raised when an approved work order settles its flight and is handed to billing. A stub for the
/// future Billing module to consume. Carries ids and the settled outcome, not aggregate graphs.
/// </summary>
public sealed record FlightSentToBilling : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required Guid WorkOrderId { get; init; }
    public required string WorkOrderNumber { get; init; }

    /// <summary>"Completed" or "Canceled".</summary>
    public required string Outcome { get; init; }
    public required Guid CustomerId { get; init; }
    public required Guid StationId { get; init; }
    public required Guid ApprovedByUserId { get; init; }
}

/// <summary>Raised when a staff member is assigned to a flight (future Notifications consumer).</summary>
public sealed record FlightEmployeeAssigned : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required Guid StaffMemberId { get; init; }
}
