using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Pricing;
using Core.Domain.Enumerations;

namespace Core.Application.Features.ManpowerPricePlan.Commands.UpdateManpowerPricePlan;

public sealed record UpdateManpowerPricePlanCommand(
    Guid Id,
    Guid ManpowerTypeId,
    Guid OperationTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<PricePlanBracketInput> Brackets) : ICommand;
