using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.OperationType;

namespace Core.Domain.Events;

public sealed class OperationTypeActivatedEvent(OperationTypeId operationTypeId) : DomainEvent
{
    public OperationTypeId OperationTypeId { get; } = operationTypeId;
}
