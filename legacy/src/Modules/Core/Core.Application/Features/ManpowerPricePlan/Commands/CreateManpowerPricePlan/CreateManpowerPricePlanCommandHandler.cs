using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Pricing;

namespace Core.Application.Features.ManpowerPricePlan.Commands.CreateManpowerPricePlan;

/// <summary>Creates a manpower price plan (aggregate invariant + uniqueness). Orchestration mirrors <see cref="Core.Application.Features.Customer.Commands.CreateCustomer.CreateCustomerCommandHandler"/>.</summary>
public sealed class CreateManpowerPricePlanCommandHandler(IManpowerPricePlanRepository plans)
    : ICommandHandler<CreateManpowerPricePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateManpowerPricePlanCommand request, CancellationToken cancellationToken)
    {
        var manpowerTypeId = ManpowerTypeId.From(request.ManpowerTypeId);
        var operationTypeId = OperationTypeId.From(request.OperationTypeId);
        var currencyId = CurrencyId.From(request.CurrencyId);

        if (await plans.ExistsForCombinationAsync(manpowerTypeId, operationTypeId, null, cancellationToken))
            return Error.Conflict("A price plan for this manpower type and operation type combination already exists.");

        var brackets = request.Brackets
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var created = Core.Domain.Aggregates.ManpowerPricePlan.ManpowerPricePlan.Create(manpowerTypeId, operationTypeId, currencyId, request.Basis, brackets);
        if (created.IsFailure) return created.Error;

        plans.Add(created.Value);
        return created.Value.Id.Value;
    }
}
