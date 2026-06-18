using Store.Domain.Enumerations;

namespace Store.Contracts.Features.Pricing;

/// <summary>
/// A system-default price plan attached to a Store item (Tool / Material / GeneralSupport),
/// exposed for the contract wizard so it can pre-fill brackets when the user selects an item.
/// Store plans are universal — no operation-type or aircraft-type dimensions — so
/// <c>OperationTypeId</c> is fixed to <see cref="Guid.Empty"/> and <c>AircraftTypeId</c> is
/// always <c>null</c>; the host shape is shared with the Core counterpart for symmetry.
/// </summary>
public sealed record PricePlanScopeOption(
    Guid PlanId,
    Guid OperationTypeId,
    Guid? AircraftTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PriceBracketDto> Brackets);
