using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Transport-only payload describing a per-OT advance payment as supplied by the
/// application layer. Validation lives inside <see cref="Contract.Create"/> /
/// <see cref="Contract.Update"/>; drafts themselves are never persisted.
/// </summary>
/// <param name="OperationTypeId">
/// Target OT — must match one of the contract's OTs (validated by the aggregate).
/// </param>
/// <param name="FlightsCount">Number of pre-paid flights; the source-of-truth for billing usage.</param>
/// <param name="FlightCost">Per-flight charge (positive) — must be a <see cref="Money"/> amount.</param>
/// <param name="Balance">Pre-paid balance bucket — primary deduction target during consumption.</param>
/// <param name="Deposit">Secured deposit bucket — overflow target after balance is exhausted.</param>
/// <param name="ExistingContractAdvancePaymentId">
/// When updating an existing row, the stable entity id so the persistence layer can
/// preserve consumption history. <c>null</c> means "create a fresh row".
/// </param>
public sealed record ContractAdvancePaymentDraft(
    Guid OperationTypeId,
    int FlightsCount,
    decimal FlightCost,
    decimal Balance,
    decimal Deposit,
    Guid? ExistingContractAdvancePaymentId);
