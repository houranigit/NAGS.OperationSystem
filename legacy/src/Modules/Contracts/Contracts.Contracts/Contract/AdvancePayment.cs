using Contracts.Domain.Enumerations;
using Core.Contracts.Features.OperationType;

namespace Contracts.Contracts.Contract;

/// <summary>
/// Public projection of a single per-OT advance payment row. Mirrors the shape of
/// <see cref="Contracts.Domain.Aggregates.Contract.ContractAdvancePayment"/> with primitive
/// money fields (no VOs cross the contract boundary).
/// </summary>
/// <param name="Id">
/// Stable identifier of the underlying child entity. The wizard uses this when the user
/// edits an existing advance-payment row to keep consumption history attached.
/// </param>
public sealed record AdvancePayment(
    Guid Id,
    Guid OperationTypeId,
    OperationTypeSnapshot OperationType,
    int FlightsCount,
    decimal FlightCost,
    decimal Balance,
    decimal Deposit,
    decimal RemainingBalance,
    decimal RemainingDeposit);
