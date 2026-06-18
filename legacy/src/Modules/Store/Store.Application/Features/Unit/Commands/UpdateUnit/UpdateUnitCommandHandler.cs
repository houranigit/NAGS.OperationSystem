using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Unit;

namespace Store.Application.Features.Unit.Commands.UpdateUnit;

public sealed class UpdateUnitCommandHandler(IUnitRepository units)
    : ICommandHandler<UpdateUnitCommand>
{
    public async Task<Result> Handle(UpdateUnitCommand request, CancellationToken cancellationToken)
    {
        var id = UnitId.From(request.Id);
        var entity = await units.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Unit was not found.");

        if (await units.ExistsByCodeAsync(request.Code, id, cancellationToken))
            return Error.Conflict("Another unit with this code already exists.");

        if (await units.ExistsByNameAsync(request.Name, id, cancellationToken))
            return Error.Conflict("Another unit with this name already exists.");

        var detailsResult = entity.UpdateDetails(request.Code, request.Name);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        units.Update(entity);
        return Result.Success();
    }
}
