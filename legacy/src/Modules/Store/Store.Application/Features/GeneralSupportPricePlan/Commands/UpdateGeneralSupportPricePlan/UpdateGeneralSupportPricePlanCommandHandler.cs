using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Store.Domain.Aggregates.GeneralSupportPricePlan;
using Store.Domain.Pricing;

namespace Store.Application.Features.GeneralSupportPricePlan.Commands.UpdateGeneralSupportPricePlan;

public sealed class UpdateGeneralSupportPricePlanCommandHandler(
    IGeneralSupportPricePlanRepository plans,
    ICurrencyReader currencies)
    : ICommandHandler<UpdateGeneralSupportPricePlanCommand>
{
    public async Task<Result> Handle(UpdateGeneralSupportPricePlanCommand request, CancellationToken cancellationToken)
    {
        var id = GeneralSupportPricePlanId.From(request.Id);
        var entity = await plans.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("General support price plan was not found.");

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
