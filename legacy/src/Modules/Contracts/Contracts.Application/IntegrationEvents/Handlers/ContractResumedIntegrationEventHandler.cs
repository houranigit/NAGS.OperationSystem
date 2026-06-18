using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractResumedIntegrationEventHandler : IIntegrationEventHandler<ContractResumedIntegrationEvent>
{
    public Task Handle(ContractResumedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
