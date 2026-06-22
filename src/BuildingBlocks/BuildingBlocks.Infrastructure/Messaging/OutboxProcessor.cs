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

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
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
                logger.LogError(ex, "Outbox dispatch failed for message {MessageId} ({Type}).", message.Id, message.Type);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
