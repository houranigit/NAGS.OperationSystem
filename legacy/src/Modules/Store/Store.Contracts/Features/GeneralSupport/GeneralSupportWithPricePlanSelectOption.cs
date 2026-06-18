using Store.Contracts.Features.Pricing;

namespace Store.Contracts.Features.GeneralSupport;

/// <summary>
/// General-support select-option enriched with its system-default price plan (typically zero
/// or one entry in <see cref="Plans"/>). Powers the contract wizard's auto-fill flow.
/// </summary>
public sealed record GeneralSupportWithPricePlanSelectOption(
    Guid Id,
    string Name,
    IReadOnlyList<PricePlanScopeOption> Plans);
