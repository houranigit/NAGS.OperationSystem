using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Store.Domain.Aggregates.ToolPricePlan;
using Store.Domain.Pricing;

namespace Store.Application.Features.ToolPricePlan.Commands.UpdateToolPricePlan;

public sealed class UpdateToolPricePlanCommandHandler(
    IToolPricePlanRepository plans,
    ICurrencyReader currencies)
    : ICommandHandler<UpdateToolPricePlanCommand>
{
    public async Task<Result> Handle(UpdateToolPricePlanCommand request, CancellationToken cancellationToken)
    {
        var id = ToolPricePlanId.From(request.Id);
        var entity = await plans.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Tool price plan was not found.");

        if (!await currencies.ExistsActiveAsync(request.CurrencyId, cancellationToken))
            return Error.Validation("The selected currency does not exist or is inactive.");

        var basicsResult = entity.UpdateBasics(request.CurrencyId, request.Basis);
        if (basicsResult.IsFailure) return basicsResult;

        var brackets = (request.Brackets ?? [])
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var bracketsResult = entity.ReplaceBrackets(brackets);
        if (bracketsResult.IsFailure) return bracketsResult;

        plans.Update(entity);
        return Result.Success();
    }
}
