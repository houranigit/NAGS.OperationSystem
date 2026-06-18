using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class CustomerIataCodeUpdatedIntegrationEventHandler : IIntegrationEventHandler<CustomerIataCodeUpdatedIntegrationEvent>
{
    public Task Handle(CustomerIataCodeUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
