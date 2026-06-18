using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Pricing;
using Core.Domain.Enumerations;

namespace Core.Application.Features.ServicePricePlan.Commands.UpdateServicePricePlan;

public sealed record UpdateServicePricePlanCommand(
    Guid Id,
    Guid ServiceId,
    Guid OperationTypeId,
    Guid? AircraftTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand;
