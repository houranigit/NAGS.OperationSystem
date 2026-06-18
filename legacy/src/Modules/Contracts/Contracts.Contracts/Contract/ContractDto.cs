using Contracts.Domain.Enumerations;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.Station;

namespace Contracts.Contracts.Contract;

public sealed record ContractDto(
    Guid Id,
    string ContractNo,
    CustomerSnapshot Customer,
    CurrencySnapshot Currency,
    ContractPeriod Period,
    ContractFees Fees,
    bool DebriefRequired,
    IReadOnlyList<AdvancePayment> AdvancePayments,
    CancellationPlan CancellationPlan,
    DelayPlan DelayPlan,
    IReadOnlyList<StationSnapshot> Stations,
    IReadOnlyList<ContractOperationTypeDto> OperationTypes,
    IReadOnlyList<ContractService> Services,
    IReadOnlyList<ContractManpower> Manpowers,
    IReadOnlyList<ContractTool> Tools,
    IReadOnlyList<ContractMaterial> Materials,
    IReadOnlyList<ContractGeneralSupport> GeneralSupports,
    ContractStatus Status,
    string? TerminationReason,
    Guid? TerminatedByUserId,
    DateTime? TerminatedAt,
    PaymentTerms PaymentTerms,
    bool ApplyVat,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    Guid? UpdatedByUserId,
    DateTime? UpdatedAt,
    byte[]? Attachment = null);

/// <summary>
/// Contract operation type shape for the DTO — includes the contract services declared
/// for flights under this OT. Used by the wizard hydration and by
/// <see cref="Contracts.Contracts.Readers.IContractReadService"/>.
/// </summary>
public sealed record ContractOperationTypeDto(
    OperationTypeSnapshot OperationType,
    IReadOnlyList<ServiceSnapshot> Services);
