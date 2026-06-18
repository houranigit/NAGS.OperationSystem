using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Domain.Events;

/// <summary>
/// Raised by the status sync job when the contract crosses its expiry date.
/// </summary>
public sealed class ContractExpiredEvent(ContractId contractId) : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
}
