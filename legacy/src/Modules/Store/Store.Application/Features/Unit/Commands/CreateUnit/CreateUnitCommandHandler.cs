using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Unit;
using StoreUnit = Store.Domain.Aggregates.Unit.Unit;

namespace Store.Application.Features.Unit.Commands.CreateUnit;

public sealed class CreateUnitCommandHandler(IUnitRepository units)
    : ICommandHandler<CreateUnitCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUnitCommand request, CancellationToken cancellationToken)
    {
        if (await units.ExistsByCodeAsync(request.Code, ct: cancellationToken))
            return Error.Conflict("A unit with this code already exists.");

        if (await units.ExistsByNameAsync(request.Name, ct: cancellationToken))
            return Error.Conflict("A unit with this name already exists.");

        var created = StoreUnit.Create(request.Code, request.Name);
        if (created.IsFailure) return created.Error;

        var unit = created.Value;

        if (!request.IsActive)
        {
            var d = unit.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        units.Add(unit);
        return unit.Id.Value;
    }
}
