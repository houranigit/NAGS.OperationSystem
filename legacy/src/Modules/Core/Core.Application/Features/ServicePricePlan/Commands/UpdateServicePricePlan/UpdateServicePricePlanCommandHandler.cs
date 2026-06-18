using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ServicePricePlan;
using Core.Domain.Pricing;

namespace Core.Application.Features.ServicePricePlan.Commands.UpdateServicePricePlan;

public sealed class UpdateServicePricePlanCommandHandler(IServicePricePlanRepository plans)
    : ICommandHandler<UpdateServicePricePlanCommand>
{
    public async Task<Result> Handle(UpdateServicePricePlanCommand request, CancellationToken cancellationToken)
    {
        var id = ServicePricePlanId.From(request.Id);
        var entity = await plans.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Service price plan was not found.");

        var currencyId = CurrencyId.From(request.CurrencyId);

        var basicsResult = entity.UpdateBasics(currencyId, request.Basis);
        if (basicsResult.IsFailure) return basicsResult;

        var brackets = request.Brackets
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var bracketsResult = entity.ReplaceBrackets(brackets);
        if (bracketsResult.IsFailure) return bracketsResult;

        plans.Update(entity);
        return Result.Success();
    }
}
