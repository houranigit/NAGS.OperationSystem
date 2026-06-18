using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.GeneralSupport;

namespace Store.Domain.Events;

public sealed class GeneralSupportActivatedEvent(GeneralSupportId generalSupportId) : DomainEvent
{
    public GeneralSupportId GeneralSupportId { get; } = generalSupportId;
}
