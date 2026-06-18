using Core.Domain.Enumerations;

namespace Core.Contracts.Features.Pricing;

/// <summary>
/// A single system-default price plan attached to an item, exposed by the contract wizard
/// so it can defaults a freshly-added pricing line. The wizard picks the most specific match
/// (e.g. for services: OperationType + AircraftType, then OperationType + null) and converts
/// the brackets into the contract currency before applying.
/// </summary>
/// <param name="OperationTypeId">
/// <see cref="Guid.Empty"/> when the item type is not OT-scoped (Tool / Material / GeneralSupport
/// in Store) — the wizard treats these plans as universal.
/// </param>
public sealed record PricePlanScopeOption(
    Guid PlanId,
    Guid OperationTypeId,
    Guid? AircraftTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PriceBracketDto> Brackets);
