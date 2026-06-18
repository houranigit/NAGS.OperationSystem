using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.ToolPricePlan.Commands.UpdateToolPricePlan;

public sealed record UpdateToolPricePlanCommand(
    Guid Id,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand;
