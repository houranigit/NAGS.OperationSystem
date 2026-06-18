using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractAdvanceBalanceDepletedIntegrationEventHandler : IIntegrationEventHandler<ContractAdvanceBalanceDepletedIntegrationEvent>
{
    public Task Handle(ContractAdvanceBalanceDepletedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
