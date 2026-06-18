using BuildingBlocks.Contracts.IntegrationEvents;
using Operations.Contracts.IntegrationEvents;

namespace Operations.Application.IntegrationEvents.Handlers;

public sealed class FlightEmployeeInvitedIntegrationEventHandler : IIntegrationEventHandler<FlightEmployeeInvitedIntegrationEvent>
{
    public Task Handle(FlightEmployeeInvitedIntegrationEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
