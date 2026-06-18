using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.GeneralSupport;

namespace Store.Domain.Events;

public sealed class GeneralSupportDeactivatedEvent(GeneralSupportId generalSupportId) : DomainEvent
{
    public GeneralSupportId GeneralSupportId { get; } = generalSupportId;
}
