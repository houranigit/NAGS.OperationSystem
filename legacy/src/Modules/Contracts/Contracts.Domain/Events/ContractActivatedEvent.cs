using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Domain.Events;

/// <summary>
/// Raised when the contract enters <c>Active</c>. <see cref="Automatic"/> is true when the
/// transition was driven by the status-sync job (Draft → Active when the start date passes).
/// </summary>
public sealed class ContractActivatedEvent(ContractId contractId, bool automatic, Guid? byUserId)
    : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public bool Automatic { get; } = automatic;
    public Guid? ByUserId { get; } = byUserId;
}
