namespace Contracts.Contracts.Contract;

/// <summary>
/// UI-friendly outcome of a "is there a contract for this flight slot?" lookup. Mirrors
/// <c>Contracts.Contracts.Readers.FindContractOutcome</c> but lives on the Application
/// surface so callers don't need to depend on the cross-module reader interface.
/// </summary>
public enum FindContractForFlightOutcome
{
    Found = 0,
    NotFound = 1,
    Ambiguous = 2,
}

/// <summary>
/// Result of <c>FindContractForFlightQuery</c>. <see cref="ContractId"/> and
/// <see cref="ContractNumber"/> are non-null only when <see cref="Outcome"/> is
/// <see cref="FindContractForFlightOutcome.Found"/>; the wizard surfaces the number
/// inline so the user sees which contract their flight will be billed against.
/// <see cref="IsAogOnly"/> is true when the resolved contract's services for this OT
/// consist of exactly one AOG service — flight assignment becomes optional in that case.
/// </summary>
public sealed record FindContractForFlightDto(
    FindContractForFlightOutcome Outcome,
    Guid? ContractId,
    string? ContractNumber,
    bool IsAogOnly);
