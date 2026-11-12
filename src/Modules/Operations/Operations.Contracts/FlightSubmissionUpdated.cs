using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>A committed change by another employee; submission and original attribution remain intact.</summary>
public sealed record FlightSubmissionUpdated : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required string FlightNumber { get; init; }
    public required Guid SubmissionId { get; init; }
    public required FlightSubmissionKind SubmissionKind { get; init; }
    public FlightSubmissionAction Action { get; init; } = FlightSubmissionAction.Updated;
    /// <summary>Null for work entries, which retain creator account identity rather than a submitting staff snapshot.</summary>
    public required Guid? SubmittedByStaffMemberId { get; init; }
    public required Guid SubmittedByUserId { get; init; }
    public required Guid UpdatedByStaffMemberId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required string UpdatedByDisplayName { get; init; }
}

public enum FlightSubmissionKind
{
    Arrival = 0,
    Departure = 1,
    Service = 2,
    Task = 3
}

public enum FlightSubmissionAction
{
    Updated = 0,
    Deleted = 1
}
