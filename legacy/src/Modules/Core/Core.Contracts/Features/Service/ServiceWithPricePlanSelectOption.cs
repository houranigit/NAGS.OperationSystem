using Core.Contracts.Features.Pricing;

namespace Core.Contracts.Features.Service;

/// <summary>
/// Service select-option enriched with the system-default price plans defined for it, so the
/// contract wizard can pre-fill brackets when the user selects a service. One row per active
/// service; <see cref="Plans"/> is empty when no plans exist.
/// </summary>
public sealed record ServiceWithPricePlanSelectOption(
    Guid Id,
    string Name,
    IReadOnlyList<PricePlanScopeOption> Plans);
