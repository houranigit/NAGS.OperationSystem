using BuildingBlocks.Contracts.Messaging;

namespace Operations.Contracts;

/// <summary>One recipient of an accepted immutable share, committed with the Operations post.</summary>
public sealed record FlightShared : IntegrationEvent
{
    public required Guid ShareId { get; init; }
    public required Guid FlightId { get; init; }
    public required string FlightNumber { get; init; }
    public required Guid StationId { get; init; }
    public required Guid StaffMemberId { get; init; }
    public required Guid SharedByUserId { get; init; }
    public required string SharedByDisplayName { get; init; }
    public bool IsStationWide { get; init; }
}
