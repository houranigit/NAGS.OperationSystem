using MediatR;

namespace BuildingBlocks.Contracts.IntegrationEvents;

public interface IIntegrationEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IntegrationEvent { }
