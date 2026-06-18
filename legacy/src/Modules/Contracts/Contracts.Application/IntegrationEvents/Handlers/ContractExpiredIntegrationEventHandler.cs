using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractExpiredIntegrationEventHandler : IIntegrationEventHandler<ContractExpiredIntegrationEvent>
{
    public Task Handle(ContractExpiredIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
