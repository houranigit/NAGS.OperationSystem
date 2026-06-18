using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractTerminatedIntegrationEventHandler : IIntegrationEventHandler<ContractTerminatedIntegrationEvent>
{
    public Task Handle(ContractTerminatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
