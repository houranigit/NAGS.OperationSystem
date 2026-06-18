using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;
using Store.Domain.Pricing;

namespace Store.Application.Features.MaterialPricePlan.Commands.CreateMaterialPricePlan;

public sealed class CreateMaterialPricePlanCommandHandler(
    IMaterialPricePlanRepository plans,
    IMaterialRepository materials,
    ICurrencyReader currencies)
    : ICommandHandler<CreateMaterialPricePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMaterialPricePlanCommand request, CancellationToken cancellationToken)
    {
        var materialId = MaterialId.From(request.MaterialId);
        var material = await materials.GetByIdAsync(materialId, cancellationToken);
        if (material is null)
            return Error.Validation("The selected material does not exist.");

        if (!await currencies.ExistsActiveAsync(request.CurrencyId, cancellationToken))
            return Error.Validation("The selected currency does not exist or is inactive.");

        if (await plans.ExistsForMaterialAsync(materialId, ct: cancellationToken))
            return Error.Conflict("A price plan for this material already exists.");

        var brackets = (request.Brackets ?? [])
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var created = Store.Domain.Aggregates.MaterialPricePlan.MaterialPricePlan.Create(
            materialId, request.CurrencyId, request.Basis, brackets);
        if (created.IsFailure) return created.Error;

        plans.Add(created.Value);
        return created.Value.Id.Value;
    }
}
