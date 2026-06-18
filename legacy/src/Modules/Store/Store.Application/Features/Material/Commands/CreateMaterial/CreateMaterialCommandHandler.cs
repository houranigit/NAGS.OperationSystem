using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.Unit;

namespace Store.Application.Features.Material.Commands.CreateMaterial;

public sealed class CreateMaterialCommandHandler(
    IMaterialRepository materials,
    IUnitRepository units,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateMaterialCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMaterialCommand request, CancellationToken cancellationToken)
    {
        var unitId = UnitId.From(request.UnitId);
        if (!await units.ExistsActiveByIdAsync(unitId, cancellationToken))
            return Error.Validation("The selected unit does not exist or is inactive.");

        if (await materials.ExistsByNameAsync(request.Name, ct: cancellationToken))
            return Error.Conflict("A material with this name already exists.");

        var created = Store.Domain.Aggregates.Material.Material.Create(request.Name, unitId);
        if (created.IsFailure) return created.Error;

        var material = created.Value;

        if (!request.IsActive)
        {
            var d = material.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        materials.Add(material);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Materials);
        return material.Id.Value;
    }
}
