using Store.Contracts.Features.Pricing;

namespace Store.Contracts.Features.Tool;

/// <summary>
/// Tool select-option enriched with its system-default price plan (typically zero or one
/// entry in <see cref="Plans"/> since Store plans are not OT-scoped). Powers the contract
/// wizard's auto-fill flow.
/// </summary>
public sealed record ToolWithPricePlanSelectOption(
    Guid Id,
    string Name,
    IReadOnlyList<PricePlanScopeOption> Plans);
