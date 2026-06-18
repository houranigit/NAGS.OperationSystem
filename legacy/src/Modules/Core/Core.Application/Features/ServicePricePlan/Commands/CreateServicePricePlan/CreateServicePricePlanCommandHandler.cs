using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Aggregates.ServicePricePlan;
using Core.Domain.Pricing;

namespace Core.Application.Features.ServicePricePlan.Commands.CreateServicePricePlan;

public sealed class CreateServicePricePlanCommandHandler(IServicePricePlanRepository plans)
    : ICommandHandler<CreateServicePricePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateServicePricePlanCommand request, CancellationToken cancellationToken)
    {
        var serviceId = ServiceId.From(request.ServiceId);
        var operationTypeId = OperationTypeId.From(request.OperationTypeId);
        var currencyId = CurrencyId.From(request.CurrencyId);
        var aircraftTypeId = request.AircraftTypeId.HasValue
            ? AircraftTypeId.From(request.AircraftTypeId.Value)
            : null;

        if (await plans.ExistsForCombinationAsync(serviceId, operationTypeId, aircraftTypeId, null, cancellationToken))
            return Error.Conflict("A price plan for this service, operation type, and aircraft type combination already exists.");

        var brackets = request.Brackets
            .Select(b => new PriceBracket(b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
            .ToList();

        var created = Core.Domain.Aggregates.ServicePricePlan.ServicePricePlan.Create(serviceId, operationTypeId, aircraftTypeId, currencyId, request.Basis, brackets);
        if (created.IsFailure) return created.Error;

        plans.Add(created.Value);
        return created.Value.Id.Value;
    }
}
