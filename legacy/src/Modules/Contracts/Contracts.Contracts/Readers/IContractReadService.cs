using Core.Contracts.Features.Service;

namespace Contracts.Contracts.Readers;

/// <summary>
/// Cross-module read surface for the Operations module to resolve a contract for a flight.
/// Implemented by <c>Contracts.Infrastructure</c>; consumed by Operations command handlers.
/// </summary>
public interface IContractReadService
{
    /// <summary>
    /// Finds the single non-terminal contract for <paramref name="customerId"/> at
    /// <paramref name="stationId"/> whose period covers <paramref name="staUtc"/> and that
    /// includes <paramref name="operationTypeId"/> in its operation types. Returns:
    /// <list type="bullet">
    ///   <item><description><see cref="FindContractOutcome.Found"/> with payload — exactly 1 match.</description></item>
    ///   <item><description><see cref="FindContractOutcome.NotFound"/> — 0 matches.</description></item>
    ///   <item><description><see cref="FindContractOutcome.Ambiguous"/> — multiple non-terminal contracts cover this slot (data error).</description></item>
    /// </list>
    /// </summary>
    Task<FindContractResult> FindActiveContractForFlightAsync(
        Guid customerId,
        Guid stationId,
        Guid operationTypeId,
        DateTimeOffset staUtc,
        CancellationToken cancellationToken = default);
}

public enum FindContractOutcome
{
    Found,
    NotFound,
    Ambiguous
}

/// <summary>
/// Outcome of a contract resolution attempt. <see cref="Contract"/> is non-null only when
/// <see cref="Outcome"/> is <see cref="FindContractOutcome.Found"/>.
/// </summary>
public sealed record FindContractResult(
    FindContractOutcome Outcome,
    ActiveContractInfo? Contract);

/// <summary>
/// Minimal payload the Operations module needs after resolving a contract: the contract id,
/// human-readable contract number (denormalised onto the flight as a snapshot so the flights
/// grid can render it without joining back to the Contracts module), currency, debrief flag,
/// and the contract services applicable to flights under the OT.
/// </summary>
public sealed record ActiveContractInfo(
    Guid ContractId,
    string ContractNumber,
    Guid CurrencyId,
    bool DebriefRequired,
    IReadOnlyList<ServiceSnapshot> OperationTypeServices);
