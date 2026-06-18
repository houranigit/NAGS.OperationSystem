using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractAdvanceDepositDepletedIntegrationEventHandler : IIntegrationEventHandler<ContractAdvanceDepositDepletedIntegrationEvent>
{
    public Task Handle(ContractAdvanceDepositDepletedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
