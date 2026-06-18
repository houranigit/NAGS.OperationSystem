using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class ServiceNameUpdatedIntegrationEventHandler : IIntegrationEventHandler<ServiceNameUpdatedIntegrationEvent>
{
    public Task Handle(ServiceNameUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
