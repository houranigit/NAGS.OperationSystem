using BuildingBlocks.Application.Abstractions.Commands;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Domain.Enumerations;

namespace Contracts.Application.Features.Contract.Commands.CreateContract;

/// <summary>
/// Wizard payload that creates a contract in one shot. Every collection is the full desired
/// state (no incremental patching) — the aggregate validates atomically.
/// </summary>
/// <param name="StationIds">All stations the contract covers (≥ 1, no duplicates).</param>
/// <param name="OperationTypes">
/// All operation types the contract covers, each with its applicable contract services
/// (≥ 1 row, no duplicate OT, no AdHoc, ≥ 1 service per OT, AOG-only-or-others-only).
/// </param>
/// <param name="Services">
/// Optional service pricing lines. Empty list means "use system default pricing for every
/// flight". Each row's OT must be in <paramref name="OperationTypes"/>; the (OT, Service,
/// AircraftType) tuple must be unique. The AOG-vs-others rule is enforced per
/// <see cref="ContractOperationTypeInput"/>, not here.
/// </param>
/// <param name="Manpowers">Optional manpower pricing lines, each scoped to one operation type.</param>
/// <param name="Tools">Optional tool pricing lines, each scoped to (OT[, AircraftType]).</param>
/// <param name="Materials">Optional material pricing lines, each scoped to one operation type.</param>
/// <param name="GeneralSupports">Optional general-support pricing lines, each scoped to one operation type.</param>
/// <param name="AdvancePayments">
/// Per-OT advance payments — empty list = no advance payments. Each row is unique by
/// <c>OperationTypeId</c> and must reference an OT included in <paramref name="OperationTypes"/>.
/// </param>
public sealed record CreateContractCommand(
    string ContractNo,
    Guid CustomerId,
    Guid CurrencyId,
    ContractPeriodInput Period,
    PaymentTerms PaymentTerms,
    bool ApplyVat,
    bool DebriefRequired,
    byte[]? Attachment,
    FeesAndRatesInput FeesAndRates,
    IReadOnlyList<AdvancePaymentInput> AdvancePayments,
    CancellationPlanInput Cancellation,
    DelayPlanInput Delay,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<ContractOperationTypeInput> OperationTypes,
    IReadOnlyList<ContractServiceInput> Services,
    IReadOnlyList<ContractManpowerInput> Manpowers,
    IReadOnlyList<ContractToolInput> Tools,
    IReadOnlyList<ContractMaterialInput> Materials,
    IReadOnlyList<ContractGeneralSupportInput> GeneralSupports) : ICommand<Guid>;
