using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>
/// Raised transactionally when one durable flight-reminder milestone becomes due. The stable event
/// id is also the reminder schedule id so outbox retries and notification delivery reuse one
/// user-visible notification identity.
/// </summary>
public sealed record FlightReminderDue : IntegrationEvent
{
    public required Guid FlightId { get; init; }
    public required string FlightNumber { get; init; }
    public required Guid StaffMemberId { get; init; }
    public required DateTimeOffset ScheduledArrivalUtc { get; init; }
    public required int LeadTimeMinutes { get; init; }
}
