using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.GeneralSupportPricePlan.Commands.UpdateGeneralSupportPricePlan;

public sealed record UpdateGeneralSupportPricePlanCommand(
    Guid Id,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand;
