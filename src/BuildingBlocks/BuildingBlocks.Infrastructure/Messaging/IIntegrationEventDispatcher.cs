using BuildingBlocks.Contracts.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>Dispatches a rehydrated integration event to every registered handler, in-process.</summary>
public interface IIntegrationEventDispatcher
{
    public Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public sealed class InProcessIntegrationEventDispatcher(IServiceProvider services) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        var handlers = services.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
                continue;

            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
            var task = (Task)method.Invoke(handler, [integrationEvent, cancellationToken])!;
            await task;
        }
    }
}
