using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Transport-only payload describing a contract material line. Mirrors
/// <see cref="ContractManpowerDraft"/> with a material reference instead of manpower type.
/// </summary>
public sealed record ContractMaterialDraft(
    OperationTypeSnapshot OperationType,
    MaterialSnapshot Material,
    PricingBasis Basis,
    Money? PackagePaidBalance,
    IReadOnlyList<ContractPriceBracket> Brackets,
    Guid? ExistingContractMaterialId);
