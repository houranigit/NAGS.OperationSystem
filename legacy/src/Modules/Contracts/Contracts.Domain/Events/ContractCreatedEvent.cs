using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Domain.Events;

public sealed class ContractCreatedEvent(ContractId contractId, Guid createdByUserId) : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public Guid CreatedByUserId { get; } = createdByUserId;
}
