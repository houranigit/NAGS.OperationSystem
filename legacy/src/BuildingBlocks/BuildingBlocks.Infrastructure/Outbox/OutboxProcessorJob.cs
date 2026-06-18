using System.Text.Json;
using BuildingBlocks.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Generic Quartz job that polls and publishes outbox messages for a specific module DbContext.
/// Each module registers its own instance: AddJob&lt;OutboxProcessorJob&lt;ModuleDbContext&gt;&gt;()
/// with a 10-second trigger.
/// </summary>
[DisallowConcurrentExecution]
public sealed class OutboxProcessorJob<TDbContext>(
    TDbContext dbContext,
    IPublisher publisher,
    ILogger<OutboxProcessorJob<TDbContext>> logger)
    : IJob
    where TDbContext : BaseDbContext
{
    private const int BatchSize = 20;

    public async Task Execute(IJobExecutionContext context)
    {
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(context.CancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var eventType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == message.Type);

                if (eventType is null)
                {
                    logger.LogWarning("Outbox: unknown event type {Type}", message.Type);
                    continue;
                }

                var integrationEvent = JsonSerializer.Deserialize(message.Content, eventType);
                if (integrationEvent is INotification notification)
                    await publisher.Publish(notification, context.CancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox: failed to process message {Id}", message.Id);
                message.Error = ex.Message;
            }
        }

        if (messages.Count > 0)
            await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
