using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.ToolPricePlan.Commands.CreateToolPricePlan;

public sealed record CreateToolPricePlanCommand(
    Guid ToolId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand<Guid>;
