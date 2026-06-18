using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Per-operation-type advance payment row. Replaces the old single
/// <c>Contract.AdvancePayment</c> VO with a child collection so the customer can pre-pay
/// independently for each operation type the contract covers (e.g. one HAJJ package and
/// one SCHEDULED package on the same contract). Each entity owns the existing
/// <see cref="ScheduledAdvancedPayment"/> VO unchanged — all the FlightsCount /
/// FlightCost / Balance / Deposit / Remaining* logic, including <c>Consume(charge)</c>,
/// stays inside the VO.
/// </summary>
public sealed class ContractAdvancePayment : Entity<ContractAdvancePaymentId>
{
    public ContractId ContractId { get; private set; } = null!;

    /// <summary>
    /// Direct OT id, persisted as its own column so EF can build the composite
    /// <c>(ContractId, OperationTypeId)</c> unique index. Always equal to
    /// <see cref="OperationType"/>'s id — kept in sync by <see cref="Create"/>.
    /// </summary>
    public Guid OperationTypeId { get; private set; }

    /// <summary>
    /// Frozen <see cref="OperationTypeSnapshot"/> tying this payment to a single OT on the
    /// contract. Preserves the historical OT name even if the source OT renames later.
    /// </summary>
    public OperationTypeSnapshot OperationType { get; private set; } = null!;

    /// <summary>The owned advance-payment VO — unchanged from before the per-OT split.</summary>
    public ScheduledAdvancedPayment Payment { get; private set; } = null!;

    private ContractAdvancePayment() { }

    /// <summary>
    /// Creates a new per-OT advance payment row. Caller is responsible for ensuring the
    /// <paramref name="operationType"/> matches one already on the contract — this is
    /// enforced one level up in <see cref="Contract"/>.
    /// </summary>
    /// <param name="id">
    /// Optional existing entity id. When supplied, EF tracks the row as an UPDATE instead
    /// of DELETE+INSERT — used by <see cref="Contract.Update"/> to preserve consumption
    /// history (and any FK references billing might add later) across save cycles. Pass
    /// <c>null</c> for brand-new rows.
    /// </param>
    internal static Result<ContractAdvancePayment> Create(
        ContractId contractId,
        ContractAdvancePaymentId? id,
        OperationTypeSnapshot operationType,
        ScheduledAdvancedPayment payment)
    {
        if (contractId is null) return Error.Validation("Contract id is required.");
        if (operationType is null) return Error.Validation("Operation type is required.");
        if (payment is null) return Error.Validation("Advance payment value is required.");

        return new ContractAdvancePayment
        {
            Id = id ?? ContractAdvancePaymentId.New(),
            ContractId = contractId,
            OperationTypeId = operationType.OperationTypeId,
            OperationType = operationType,
            Payment = payment,
        };
    }

    /// <summary>
    /// Replaces the inner VO with a new value. Used by <c>Contract.Update</c> after
    /// rehydrating remaining balances so we keep the same entity row (and id) instead of
    /// orphaning consumption history.
    /// </summary>
    internal void ReplaceWith(ScheduledAdvancedPayment payment)
    {
        if (payment is null)
            throw new ArgumentNullException(nameof(payment));
        Payment = payment;
    }
}
