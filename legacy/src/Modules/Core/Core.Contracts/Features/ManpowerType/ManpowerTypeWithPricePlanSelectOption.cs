using Core.Contracts.Features.Pricing;

namespace Core.Contracts.Features.ManpowerType;

/// <summary>
/// Manpower-type select-option enriched with the system-default price plans defined for it
/// (one plan per OperationType). The contract wizard uses these to pre-fill brackets.
/// </summary>
public sealed record ManpowerTypeWithPricePlanSelectOption(
    Guid Id,
    string Name,
    IReadOnlyList<PricePlanScopeOption> Plans);
