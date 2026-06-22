namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Idempotency record on the consuming side. A cross-module handler records
/// (<see cref="MessageId"/>, <see cref="Consumer"/>) when it processes an integration event and
/// skips re-processing if the pair already exists, so redelivery causes no duplicate side effects.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }
    public string Consumer { get; init; } = null!;
    public DateTimeOffset ProcessedOnUtc { get; init; }
}
