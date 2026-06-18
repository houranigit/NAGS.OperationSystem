using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Domain.Events;

public sealed class ContractUpdatedEvent(ContractId contractId, Guid updatedByUserId) : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public Guid UpdatedByUserId { get; } = updatedByUserId;
}
