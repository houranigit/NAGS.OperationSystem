using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Enumerations;

namespace Contracts.Domain.Events;

/// <summary>
/// Raised when a Suspended contract is manually resumed. Carries the recomputed status so
/// downstream listeners (notifications, billing) know whether the contract is now Draft,
/// Active, or already Expired.
/// </summary>
public sealed class ContractResumedEvent(ContractId contractId, ContractStatus newStatus, Guid byUserId)
    : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public ContractStatus NewStatus { get; } = newStatus;
    public Guid ByUserId { get; } = byUserId;
}
