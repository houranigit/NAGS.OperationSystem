using Core.Contracts.Features.Currency;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Domain.Enumerations;

namespace Core.Contracts.Features.ManpowerPricePlan;

public sealed record ManpowerPricePlanLightDto(
    Guid Id,
    ManpowerTypeSnapshot ManpowerTypeSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
