using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Pricing;
using Core.Domain.Enumerations;

namespace Core.Application.Features.ManpowerPricePlan.Commands.CreateManpowerPricePlan;

public sealed record CreateManpowerPricePlanCommand(
    Guid ManpowerTypeId,
    Guid OperationTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand<Guid>;
