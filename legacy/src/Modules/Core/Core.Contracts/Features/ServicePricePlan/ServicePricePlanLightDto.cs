using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Domain.Enumerations;

namespace Core.Contracts.Features.ServicePricePlan;

public sealed record ServicePricePlanLightDto(
    Guid Id,
    ServiceSnapshot ServiceTypeSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeSnapshot,
    CurrencySnapshot CurrencySnapshot,
    PricingBasis Basis,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
