using Core.Contracts.Features.Currency;
using Store.Contracts.Features.Material;
using Store.Domain.Enumerations;

namespace Store.Contracts.Features.MaterialPricePlan;

public sealed record MaterialPricePlanDto(
    Guid Id,
    MaterialSnapshot MaterialSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    bool IsActive,
    IReadOnlyList<MaterialPricePlanBracketDto> Brackets,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
