using BuildingBlocks.Domain.Events;
using Store.Domain.Aggregates.Material;

namespace Store.Domain.Events;

public sealed class MaterialCreatedEvent(MaterialId materialId) : DomainEvent
{
    public MaterialId MaterialId { get; } = materialId;
}
