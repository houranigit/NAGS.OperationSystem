using Contracts.Domain.Enumerations;

namespace Contracts.Application.Features.Contract.Shared;

/// <summary>Wizard payload describing the contract period.</summary>
public sealed record ContractPeriodInput(
    DateTimeOffset StartDate,
    DateTimeOffset ExpiryDate,
    int ExpiryAlertDays,
    ExpiryAlertInterval? ExpiryAlertInterval);

/// <summary>
/// Wizard payload describing one row on the "Operation types" step:
/// an OT plus the contract services applicable to flights under that OT. Validation lives
/// in <see cref="Contracts.Domain.Aggregates.Contract.ContractOperationType"/>.
/// </summary>
public sealed record ContractOperationTypeInput(
    Guid OperationTypeId,
    IReadOnlyList<Guid> ServiceIds);

/// <summary>Wizard payload describing the six fees and discounts.</summary>
public sealed record FeesAndRatesInput(
    FeeInput AdminFee,
    FeeInput DisbursementFee,
    FeeInput HolidayFee,
    FeeInput NightFee,
    FeeInput ReturnToRampDiscount,
    FeeInput OtherDiscount);

/// <summary>Wizard payload describing a single fee or discount row.</summary>
public sealed record FeeInput(FeeType Type, decimal Value);

/// <summary>
/// Wizard payload describing one per-OT advance payment row. The contract may carry zero
/// or more of these (with one row per <see cref="OperationTypeId"/>) — see
/// <see cref="Contracts.Domain.Aggregates.Contract.ContractAdvancePayment"/>.
/// </summary>
public sealed record AdvancePaymentInput(
    Guid OperationTypeId,
    int FlightsCount,
    decimal FlightCost,
    decimal Balance,
    decimal Deposit,
    Guid? ExistingContractAdvancePaymentId);

/// <summary>Wizard payload describing the cancellation charge plan.</summary>
public sealed record CancellationPlanInput(
    CancellationChargeBasis Basis,
    FeeType ChargeType,
    IReadOnlyList<CancellationBracketInput> Brackets);

public sealed record CancellationBracketInput(int MinMinutes, int? MaxMinutes, decimal Value);

/// <summary>Wizard payload describing the delay charge plan.</summary>
public sealed record DelayPlanInput(
    DelayType DelayType,
    DelayChargeBasis Basis,
    FeeType ChargeType,
    IReadOnlyList<DelayBracketInput> Brackets);

public sealed record DelayBracketInput(int MinMinutes, int? MaxMinutes, decimal Value);

/// <summary>Wizard payload describing one contract service line.</summary>
public sealed record ContractServiceInput(
    Guid OperationTypeId,
    Guid ServiceId,
    Guid? AircraftTypeId,
    PricingBasis Basis,
    decimal? PackagePaidBalance,
    IReadOnlyList<PriceBracketInput> Brackets,
    Guid? ExistingContractServiceId);

/// <summary>Wizard payload describing one contract manpower line.</summary>
public sealed record ContractManpowerInput(
    Guid OperationTypeId,
    Guid ManpowerTypeId,
    PricingBasis Basis,
    decimal? PackagePaidBalance,
    IReadOnlyList<PriceBracketInput> Brackets,
    Guid? ExistingContractManpowerId);

/// <summary>Wizard payload describing one contract tool line.</summary>
public sealed record ContractToolInput(
    Guid OperationTypeId,
    Guid ToolId,
    Guid? AircraftTypeId,
    PricingBasis Basis,
    decimal? PackagePaidBalance,
    IReadOnlyList<PriceBracketInput> Brackets,
    Guid? ExistingContractToolId);

/// <summary>Wizard payload describing one contract material line.</summary>
public sealed record ContractMaterialInput(
    Guid OperationTypeId,
    Guid MaterialId,
    PricingBasis Basis,
    decimal? PackagePaidBalance,
    IReadOnlyList<PriceBracketInput> Brackets,
    Guid? ExistingContractMaterialId);

/// <summary>Wizard payload describing one contract general-support line.</summary>
public sealed record ContractGeneralSupportInput(
    Guid OperationTypeId,
    Guid GeneralSupportId,
    PricingBasis Basis,
    decimal? PackagePaidBalance,
    IReadOnlyList<PriceBracketInput> Brackets,
    Guid? ExistingContractGeneralSupportId);

/// <summary>Wizard payload describing a single price-plan bracket.</summary>
public sealed record PriceBracketInput(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal PriceValue,
    decimal? PackagePriceValue,
    BracketBillingMode BillingMode);
