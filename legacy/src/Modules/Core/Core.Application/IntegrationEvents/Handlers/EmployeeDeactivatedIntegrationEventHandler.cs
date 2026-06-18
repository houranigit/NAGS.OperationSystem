using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;

namespace Core.Application.IntegrationEvents.Handlers;

public sealed class EmployeeDeactivatedIntegrationEventHandler : IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>
{
    public Task Handle(EmployeeDeactivatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
