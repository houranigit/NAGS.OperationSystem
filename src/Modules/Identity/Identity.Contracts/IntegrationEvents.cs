using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Messaging;

namespace Identity.Contracts;

/// <summary>
/// Raised by Identity after it provisions a portal account for a MasterData record. The owning
/// module stores <see cref="UserId"/> against the record identified by <see cref="ExternalReferenceId"/>.
/// </summary>
public sealed record PortalUserProvisioned : IntegrationEvent
{
    public required Guid ExternalReferenceId { get; init; }
    public required Guid UserId { get; init; }
    public required UserType UserType { get; init; }
    public required string Email { get; init; }

    /// <summary>Echoes the originating request's correlation id so stale replies can be ignored.</summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>Raised when a portal account is deactivated (so the owning module can reflect lost access).</summary>
public sealed record PortalUserDeactivated : IntegrationEvent
{
    public required Guid ExternalReferenceId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Raised when Identity cannot provision a requested portal account (bad email, missing/incompatible
/// role, duplicate email). Lets the owning module surface a visible, retryable failure.
/// </summary>
public sealed record PortalUserProvisioningFailed : IntegrationEvent
{
    public required Guid ExternalReferenceId { get; init; }
    public required UserType UserType { get; init; }
    public required string Reason { get; init; }
    public Guid CorrelationId { get; init; }
}
