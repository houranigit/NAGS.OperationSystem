using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class OperationTypeNameUpdatedIntegrationEventHandler : IIntegrationEventHandler<OperationTypeNameUpdatedIntegrationEvent>
{
    public Task Handle(OperationTypeNameUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
