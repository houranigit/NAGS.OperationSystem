using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.Unit;

namespace Store.Application.Features.Material.Commands.UpdateMaterial;

public sealed class UpdateMaterialCommandHandler(
    IMaterialRepository materials,
    IUnitRepository units,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateMaterialCommand>
{
    public async Task<Result> Handle(UpdateMaterialCommand request, CancellationToken cancellationToken)
    {
        var id = MaterialId.From(request.Id);
        var entity = await materials.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Material was not found.");

        var unitId = UnitId.From(request.UnitId);
        if (!await units.ExistsActiveByIdAsync(unitId, cancellationToken))
            return Error.Validation("The selected unit does not exist or is inactive.");

        if (await materials.ExistsByNameAsync(request.Name, id, cancellationToken))
            return Error.Conflict("Another material with this name already exists.");

        var detailsResult = entity.UpdateDetails(request.Name, unitId);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        materials.Update(entity);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Materials);
        return Result.Success();
    }
}
