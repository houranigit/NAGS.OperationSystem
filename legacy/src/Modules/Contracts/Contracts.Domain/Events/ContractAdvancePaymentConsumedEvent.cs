using BuildingBlocks.Domain.Events;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Events;

/// <summary>
/// Raised when <see cref="Contract.ConsumeAdvance"/> deducts from a per-OT prepaid bucket.
/// <see cref="OperationTypeId"/> identifies which advance-payment row was consumed —
/// downstream listeners (billing, audit) need it because a single contract can now hold
/// multiple advance payments scoped per operation type.
/// </summary>
public sealed class ContractAdvancePaymentConsumedEvent(
    ContractId contractId,
    Guid operationTypeId,
    Money fromBalance,
    Money fromDeposit,
    Money shortfall,
    bool balanceDepleted,
    bool depositDepleted) : DomainEvent
{
    public ContractId ContractId { get; } = contractId;
    public Guid OperationTypeId { get; } = operationTypeId;
    public Money FromBalance { get; } = fromBalance;
    public Money FromDeposit { get; } = fromDeposit;
    public Money Shortfall { get; } = shortfall;
    public bool BalanceDepleted { get; } = balanceDepleted;
    public bool DepositDepleted { get; } = depositDepleted;
}
