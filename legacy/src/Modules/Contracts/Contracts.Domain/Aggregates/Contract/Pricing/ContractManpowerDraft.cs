using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Transport-only payload describing a contract manpower line. Same shape as
/// <see cref="ContractServiceDraft"/> minus the AircraftType — pricing is per-OT only.
/// </summary>
public sealed record ContractManpowerDraft(
    OperationTypeSnapshot OperationType,
    ManpowerTypeSnapshot ManpowerType,
    PricingBasis Basis,
    Money? PackagePaidBalance,
    IReadOnlyList<ContractPriceBracket> Brackets,
    Guid? ExistingContractManpowerId);
