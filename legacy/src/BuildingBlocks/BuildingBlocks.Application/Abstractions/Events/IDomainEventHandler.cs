using BuildingBlocks.Domain.Events;
using MediatR;

namespace BuildingBlocks.Application.Abstractions.Events;

public interface IDomainEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : DomainEvent { }
