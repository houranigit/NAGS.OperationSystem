using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Transport-only payload describing one row on the wizard's "Operation types" step:
/// an OT plus the contract services applicable to it. Validation lives inside the
/// aggregate factory; drafts themselves are never persisted.
/// </summary>
/// <param name="OperationType">Frozen snapshot of the operation type.</param>
/// <param name="Services">
/// Contract services declared for flights under this OT. Must be ≥ 1 entry, must not
/// duplicate, and must be either AOG-only or all-non-AOG (no mix).
/// </param>
public sealed record ContractOperationTypeDraft(
    OperationTypeSnapshot OperationType,
    IReadOnlyList<ServiceSnapshot> Services);
