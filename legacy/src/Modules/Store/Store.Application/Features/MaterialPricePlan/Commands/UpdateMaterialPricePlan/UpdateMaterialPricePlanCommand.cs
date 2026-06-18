using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.MaterialPricePlan.Commands.UpdateMaterialPricePlan;

public sealed record UpdateMaterialPricePlanCommand(
    Guid Id,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand;
