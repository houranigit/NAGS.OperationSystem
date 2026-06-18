using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class CustomerDeactivatedIntegrationEventHandler : IIntegrationEventHandler<CustomerDeactivatedIntegrationEvent>
{
    public Task Handle(CustomerDeactivatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
