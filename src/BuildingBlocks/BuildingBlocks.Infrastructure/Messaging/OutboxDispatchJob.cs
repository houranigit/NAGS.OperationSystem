using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Quartz job (runs every few seconds) that drains every registered module outbox. Each module's
/// processor runs in its own DI scope so it uses a fresh, module-scoped DbContext.
/// </summary>
[DisallowConcurrentExecution]
public sealed class OutboxDispatchJob(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatchJob> logger) : IJob
{
    public static readonly JobKey Key = new("outbox-dispatch");

    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processors = scope.ServiceProvider.GetServices<IOutboxProcessor>();

        foreach (var processor in processors)
        {
            try
            {
                await processor.ProcessAsync(context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox processor {Processor} failed.", processor.GetType().Name);
            }
        }
    }
}
