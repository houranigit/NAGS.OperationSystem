using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;
using Store.Domain.Pricing;

namespace Store.Application.Features.ToolPricePlan.Commands.CreateToolPricePlan;

public sealed class CreateToolPricePlanCommandHandler(
    IToolPricePlanRepository plans,
    IToolRepository tools,
    ICurrencyReader currencies)
    : ICommandHandler<CreateToolPricePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateToolPricePlanCommand request, CancellationToken cancellationToken)
    {
        var toolId = ToolId.From(request.ToolId);
        var tool = await tools.GetByIdAsync(toolId, cancellationToken);
        if (tool is null)
            return Error.Validation("The selected tool does not exist.");

        if (!await currencies.ExistsActiveAsync(request.CurrencyId, cancellationToken))
            return Error.Validation("The selected currency does not exist or is inactive.");

        if (await plans.ExistsForToolAsync(toolId, ct: cancellationToken))
            return Error.Conflict("A price plan for this tool already exists.");

        var brackets = (request.Brackets ?? [])
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var created = Store.Domain.Aggregates.ToolPricePlan.ToolPricePlan.Create(
            toolId, request.CurrencyId, request.Basis, brackets);
        if (created.IsFailure) return created.Error;

        plans.Add(created.Value);
        return created.Value.Id.Value;
    }
}
