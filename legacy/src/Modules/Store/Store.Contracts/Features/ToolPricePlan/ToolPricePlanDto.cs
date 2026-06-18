using Core.Contracts.Features.Currency;
using Store.Contracts.Features.Tool;
using Store.Domain.Enumerations;

namespace Store.Contracts.Features.ToolPricePlan;

public sealed record ToolPricePlanDto(
    Guid Id,
    ToolSnapshot ToolSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    bool IsActive,
    IReadOnlyList<ToolPricePlanBracketDto> Brackets,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
