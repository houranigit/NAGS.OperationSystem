using BuildingBlocks.Contracts.IntegrationEvents;
using Contracts.Contracts.IntegrationEvents;

namespace Contracts.Application.IntegrationEvents.Handlers;

public sealed class ContractExpiringSoonIntegrationEventHandler : IIntegrationEventHandler<ContractExpiringSoonIntegrationEvent>
{
    public Task Handle(ContractExpiringSoonIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
