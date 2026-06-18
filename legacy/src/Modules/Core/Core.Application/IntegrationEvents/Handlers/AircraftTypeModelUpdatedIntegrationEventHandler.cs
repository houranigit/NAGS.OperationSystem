using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class AircraftTypeModelUpdatedIntegrationEventHandler : IIntegrationEventHandler<AircraftTypeModelUpdatedIntegrationEvent>
{
    public Task Handle(AircraftTypeModelUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
