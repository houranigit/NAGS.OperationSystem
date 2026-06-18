using Core.Contracts.Features.Currency;
using Store.Contracts.Features.GeneralSupport;
using Store.Domain.Enumerations;

namespace Store.Contracts.Features.GeneralSupportPricePlan;

public sealed record GeneralSupportPricePlanDto(
    Guid Id,
    GeneralSupportSnapshot GeneralSupportSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    bool IsActive,
    IReadOnlyList<GeneralSupportPricePlanBracketDto> Brackets,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
