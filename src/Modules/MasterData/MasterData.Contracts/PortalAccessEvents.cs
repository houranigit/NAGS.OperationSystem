using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Messaging;

namespace MasterData.Contracts;

/// <summary>
/// Raised by MasterData when an administrator requests portal access for a StaffMember or
/// CustomerContact. Identity consumes it, provisions an invited User of <see cref="UserType"/> with
/// the chosen <see cref="RoleId"/>, and replies with a provisioning event.
/// </summary>
public sealed record PortalAccessRequested : IntegrationEvent
{
    /// <summary>
    /// The authenticated Identity user who initiated this delegation. Identity resolves this user
    /// again when processing the event so role permissions changed after the request cannot bypass
    /// the live permission ceiling.
    /// </summary>
    public required Guid InitiatedByUserId { get; init; }

    public required Guid ExternalReferenceId { get; init; }
    public required UserType UserType { get; init; }
    public required Guid RoleId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Identifies this provisioning attempt so a stale reply cannot overwrite a newer request.</summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>Raised when a linked MasterData record's email changes, so Identity can re-verify it.</summary>
public sealed record LinkedEmailChangeRequested : IntegrationEvent
{
    public required Guid ExternalReferenceId { get; init; }
    public required Guid UserId { get; init; }
    public required string NewEmail { get; init; }
}

/// <summary>Raised when a linked MasterData record is deactivated/removed, so Identity revokes access.</summary>
public sealed record LinkedRecordDeactivated : IntegrationEvent
{
    public required Guid ExternalReferenceId { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>True when the record is permanently removed and its email should be released for reuse.</summary>
    public bool ReleaseEmail { get; init; }
}
