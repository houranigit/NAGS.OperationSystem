using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Domain.Events;

public sealed class ContractSuspendedEvent(ContractId contractId, string reason, Guid byUserId)
    : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public string Reason { get; } = reason;
    public Guid ByUserId { get; } = byUserId;
}
