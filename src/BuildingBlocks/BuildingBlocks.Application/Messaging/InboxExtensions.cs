using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Application.Messaging;

/// <summary>Inbox idempotency helpers for cross-module integration-event handlers.</summary>
public static class InboxExtensions
{
    public static Task<bool> HasProcessedAsync(this IOutboxDbContext db, Guid messageId, string consumer, CancellationToken cancellationToken = default) =>
        db.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);

    /// <summary>Records that <paramref name="consumer"/> processed <paramref name="messageId"/>. Caller saves.</summary>
    public static void MarkProcessed(this IOutboxDbContext db, Guid messageId, string consumer, TimeProvider timeProvider) =>
        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedOnUtc = timeProvider.GetUtcNow()
        });

    /// <summary>Enqueues an integration event in the module outbox (committed with the current transaction).</summary>
    public static void Enqueue(this IOutboxDbContext db, BuildingBlocks.Contracts.Messaging.IntegrationEvent integrationEvent) =>
        db.OutboxMessages.Add(OutboxMessage.Create(integrationEvent));
}
