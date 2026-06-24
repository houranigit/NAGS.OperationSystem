using BuildingBlocks.Contracts.Messaging;

namespace BuildingBlocks.Contracts.Email;

/// <summary>
/// A durable request to deliver one email, written to a module's outbox in the same transaction as
/// the change that triggered it. The body is stored encrypted (Data Protection) so secrets carried
/// in links (e.g. activation/reset tokens) are never persisted in cleartext. Delivery is retried by
/// the outbox processor until it succeeds.
/// </summary>
public sealed record EmailDeliveryRequested : IntegrationEvent
{
    public required string ToEmail { get; init; }
    public required string ToName { get; init; }
    public required string Subject { get; init; }

    /// <summary>Data-Protection-encrypted HTML body. Decrypted only at the moment of sending.</summary>
    public required string ProtectedBody { get; init; }

    /// <summary>Non-sensitive classification (e.g. "invitation", "password-reset") for diagnostics.</summary>
    public string? Kind { get; init; }
}
