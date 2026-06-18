using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Transport-only payload describing a contract tool line. Mirrors
/// <see cref="ContractServiceDraft"/> with a tool reference instead of a service.
/// </summary>
public sealed record ContractToolDraft(
    OperationTypeSnapshot OperationType,
    ToolSnapshot Tool,
    AircraftTypeSnapshot? AircraftType,
    PricingBasis Basis,
    Money? PackagePaidBalance,
    IReadOnlyList<ContractPriceBracket> Brackets,
    Guid? ExistingContractToolId);
