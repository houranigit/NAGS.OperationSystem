using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractSuspendedIntegrationEventHandler : IIntegrationEventHandler<ContractSuspendedIntegrationEvent>
{
    public Task Handle(ContractSuspendedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
