using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractAdvanceShortfallIntegrationEventHandler : IIntegrationEventHandler<ContractAdvanceShortfallIntegrationEvent>
{
    public Task Handle(ContractAdvanceShortfallIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
