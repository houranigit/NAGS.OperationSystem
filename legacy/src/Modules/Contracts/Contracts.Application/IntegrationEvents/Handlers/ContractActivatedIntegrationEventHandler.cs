using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractActivatedIntegrationEventHandler : IIntegrationEventHandler<ContractActivatedIntegrationEvent>
{
    public Task Handle(ContractActivatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
