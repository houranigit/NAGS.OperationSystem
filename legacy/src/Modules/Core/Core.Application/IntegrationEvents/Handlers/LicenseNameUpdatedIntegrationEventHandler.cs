using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class LicenseNameUpdatedIntegrationEventHandler : IIntegrationEventHandler<LicenseNameUpdatedIntegrationEvent>
{
    public Task Handle(LicenseNameUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
