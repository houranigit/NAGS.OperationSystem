using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Pricing;

namespace Core.Application.Features.ManpowerPricePlan.Commands.UpdateManpowerPricePlan;

/// <summary>Updates currency, basis, and bracket ladder; no <c>SaveChanges</c> here — <c>TransactionBehavior</c> persists.</summary>
public sealed class UpdateManpowerPricePlanCommandHandler(IManpowerPricePlanRepository plans)
    : ICommandHandler<UpdateManpowerPricePlanCommand>
{
    public async Task<Result> Handle(UpdateManpowerPricePlanCommand request, CancellationToken cancellationToken)
    {
        var id = ManpowerPricePlanId.From(request.Id);
        var entity = await plans.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Manpower price plan was not found.");

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
