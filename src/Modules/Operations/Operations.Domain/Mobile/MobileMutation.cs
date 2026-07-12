namespace Operations.Domain.Mobile;

/// <summary>
/// Idempotency record for one mobile outbox mutation. The mobile client generates a unique
/// <see cref="ClientMutationId"/> when it queues a write offline; the server persists this record
/// in the same transaction as the business change, so a replayed request (retry after a network
/// failure) is answered from the record instead of duplicating the mutation.
/// </summary>
public sealed class MobileMutation
{
    private MobileMutation() { }

    public string ClientMutationId { get; private set; } = null!;

    public Guid OwnerUserId { get; private set; }

    /// <summary>Mutation kind, for diagnostics (e.g. submit-work-order, cancel-flight).</summary>
    public string Kind { get; private set; } = null!;

    public Guid? WorkOrderId { get; private set; }

    public Guid? FlightId { get; private set; }

    /// <summary>Client-generated flight identity for scratch ad-hoc creates (duplicate guard across retries).</summary>
    public Guid? ClientFlightId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MobileMutation Record(
        string clientMutationId,
        Guid ownerUserId,
        string kind,
        Guid? workOrderId,
        Guid? flightId,
        Guid? clientFlightId,
        DateTimeOffset now) =>
        new()
        {
            ClientMutationId = clientMutationId,
            OwnerUserId = ownerUserId,
            Kind = kind,
            WorkOrderId = workOrderId,
            FlightId = flightId,
            ClientFlightId = clientFlightId,
            CreatedAtUtc = now
        };
}
