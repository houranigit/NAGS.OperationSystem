using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class CustomerContactRemovedIntegrationEventHandler : IIntegrationEventHandler<CustomerContactRemovedIntegrationEvent>
{
    public Task Handle(CustomerContactRemovedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
