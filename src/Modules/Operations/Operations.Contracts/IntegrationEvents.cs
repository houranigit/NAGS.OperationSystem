using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>
/// Raised transactionally when a staff member is newly added to a flight roster. Notifications
/// resolves the linked portal user and suppresses self-assignment after receiving this event.
/// </summary>
public sealed record FlightEmployeeAssigned : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required string FlightNumber { get; init; }
    public required Guid StaffMemberId { get; init; }
    public Guid? AssignedByUserId { get; init; }
    public Guid? AssignedByStaffMemberId { get; init; }
    public string? AssignedByDisplayName { get; init; }
    public FlightAssignmentSource Source { get; init; } = FlightAssignmentSource.Roster;
}

/// <summary>Raised per assigned staff member for bulk additions or a meaningful schedule edit.</summary>
public sealed record FlightScheduleUpdated : IntegrationEvent
{
    public required Guid StaffMemberId { get; init; }
    public required int FlightCount { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    /// <summary>The edited flight; omitted by existing bulk-addition envelopes.</summary>
    public Guid? FlightId { get; init; }
    public string? FlightNumber { get; init; }
}

/// <summary>Describes the user action that added the employee without coupling Operations to notification UI.</summary>
public enum FlightAssignmentSource
{
    Roster = 0,
    Invite = 1
}
