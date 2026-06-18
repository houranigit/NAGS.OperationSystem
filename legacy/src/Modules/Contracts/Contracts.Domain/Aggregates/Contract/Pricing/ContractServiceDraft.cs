using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Transport-only payload describing a contract service line as supplied by the application
/// layer. Validation lives inside <see cref="Contract.Create"/> / <see cref="Contract.Update"/>;
/// drafts themselves are never persisted.
/// </summary>
public sealed record ContractServiceDraft(
    OperationTypeSnapshot OperationType,
    ServiceSnapshot Service,
    AircraftTypeSnapshot? AircraftType,
    PricingBasis Basis,
    Money? PackagePaidBalance,
    IReadOnlyList<ContractPriceBracket> Brackets,
    Guid? ExistingContractServiceId);
