using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;
using Store.Domain.Pricing;

namespace Store.Application.Features.GeneralSupportPricePlan.Commands.CreateGeneralSupportPricePlan;

public sealed class CreateGeneralSupportPricePlanCommandHandler(
    IGeneralSupportPricePlanRepository plans,
    IGeneralSupportRepository generalSupports,
    ICurrencyReader currencies)
    : ICommandHandler<CreateGeneralSupportPricePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGeneralSupportPricePlanCommand request, CancellationToken cancellationToken)
    {
        var generalSupportId = GeneralSupportId.From(request.GeneralSupportId);
        var generalSupport = await generalSupports.GetByIdAsync(generalSupportId, cancellationToken);
        if (generalSupport is null)
            return Error.Validation("The selected general support does not exist.");

        if (!await currencies.ExistsActiveAsync(request.CurrencyId, cancellationToken))
            return Error.Validation("The selected currency does not exist or is inactive.");

        if (await plans.ExistsForGeneralSupportAsync(generalSupportId, ct: cancellationToken))
            return Error.Conflict("A price plan for this general support already exists.");

        var brackets = (request.Brackets ?? [])
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var created = Store.Domain.Aggregates.GeneralSupportPricePlan.GeneralSupportPricePlan.Create(
            generalSupportId, request.CurrencyId, request.Basis, brackets);
        if (created.IsFailure) return created.Error;

        plans.Add(created.Value);
        return created.Value.Id.Value;
    }
}
