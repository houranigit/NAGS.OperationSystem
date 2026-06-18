using BuildingBlocks.Application.Abstractions.Commands;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Domain.Enumerations;

namespace Contracts.Application.Features.Contract.Commands.UpdateContract;

/// <summary>
/// Replaces every mutable aspect of an existing contract. Same wizard payload as
/// <c>CreateContractCommand</c> plus the contract id. Refused when the contract is
/// already <c>Terminated</c> or <c>Expired</c>.
/// </summary>
public sealed record UpdateContractCommand(
    Guid Id,
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
    IReadOnlyList<ContractGeneralSupportInput> GeneralSupports) : ICommand;
