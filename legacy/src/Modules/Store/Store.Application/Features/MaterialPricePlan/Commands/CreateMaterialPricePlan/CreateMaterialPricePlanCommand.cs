using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.MaterialPricePlan.Commands.CreateMaterialPricePlan;

public sealed record CreateMaterialPricePlanCommand(
    Guid MaterialId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand<Guid>;
