using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>Drains one module's outbox and dispatches each message. One implementation per module.</summary>
public interface IOutboxProcessor
{
    public Task ProcessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic outbox processor over a module DbContext. Loads unprocessed messages oldest-first,
/// rehydrates the integration event from its stored type, dispatches it, and marks it processed.
/// Failures are recorded and retried on the next cycle.
/// </summary>
public sealed class OutboxProcessor<TDbContext>(
    TDbContext db,
    IIntegrationEventDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TDbContext>> logger) : IOutboxProcessor
    where TDbContext : DbContext, IOutboxDbContext
{
    private const int BatchSize = 50;

    /// <summary>
    /// After this many failed attempts a message is treated as dead-lettered: it is no longer retried
    /// and remains in the outbox with its <see cref="OutboxMessage.Error"/> for operator visibility.
    /// </summary>
    public const int MaxAttempts = 10;

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        // Skip dead-lettered messages (Attempts >= MaxAttempts); they stay visible but are not retried.
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return;

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type)
                    ?? throw new InvalidOperationException($"Unknown integration event type '{message.Type}'.");

                if (JsonSerializer.Deserialize(message.Content, type) is not IntegrationEvent integrationEvent)
                    throw new InvalidOperationException($"Could not deserialize integration event '{message.Type}'.");

                await dispatcher.DispatchAsync(integrationEvent, cancellationToken);

                message.ProcessedOnUtc = timeProvider.GetUtcNow();
                message.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (message.Attempts >= MaxAttempts)
                    logger.LogCritical(ex, "Outbox message {MessageId} ({Type}) dead-lettered after {Attempts} attempts.", message.Id, message.Type, message.Attempts);
                else
                    logger.LogError(ex, "Outbox dispatch failed for message {MessageId} ({Type}) (attempt {Attempts}).", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
