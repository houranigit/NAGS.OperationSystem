using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.ManpowerType;

namespace Core.Domain.Events;

public sealed class ManpowerTypeCreatedEvent(ManpowerTypeId manpowerTypeId) : DomainEvent
{
    public ManpowerTypeId ManpowerTypeId { get; } = manpowerTypeId;
}
