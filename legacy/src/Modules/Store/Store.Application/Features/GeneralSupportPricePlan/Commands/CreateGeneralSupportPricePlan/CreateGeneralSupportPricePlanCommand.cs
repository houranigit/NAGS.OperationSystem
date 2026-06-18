using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Pricing;
using Store.Domain.Enumerations;

namespace Store.Application.Features.GeneralSupportPricePlan.Commands.CreateGeneralSupportPricePlan;

public sealed record CreateGeneralSupportPricePlanCommand(
    Guid GeneralSupportId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand<Guid>;
