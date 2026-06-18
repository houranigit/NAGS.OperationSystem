using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class CustomerContactUserCreationRequestedIntegrationEventHandler : IIntegrationEventHandler<CustomerContactUserCreationRequestedIntegrationEvent>
{
    public Task Handle(CustomerContactUserCreationRequestedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
