using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Transport-only payload describing a contract general-support line. Mirrors
/// <see cref="ContractManpowerDraft"/> with a general-support reference instead of manpower type.
/// </summary>
public sealed record ContractGeneralSupportDraft(
    OperationTypeSnapshot OperationType,
    GeneralSupportSnapshot GeneralSupport,
    PricingBasis Basis,
    Money? PackagePaidBalance,
    IReadOnlyList<ContractPriceBracket> Brackets,
    Guid? ExistingContractGeneralSupportId);
