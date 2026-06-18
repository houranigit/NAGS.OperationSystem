using Core.Contracts.Features.Currency;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Domain.Enumerations;

namespace Core.Contracts.Features.ManpowerPricePlan;

public sealed record ManpowerPricePlanDto(
    Guid Id,
    ManpowerTypeSnapshot ManpowerTypeSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    IReadOnlyList<ManpowerPricePlanBracketDto> Brackets,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
