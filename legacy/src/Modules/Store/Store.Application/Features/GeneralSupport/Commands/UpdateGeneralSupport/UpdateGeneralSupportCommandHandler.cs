using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.Unit;

namespace Store.Application.Features.GeneralSupport.Commands.UpdateGeneralSupport;

public sealed class UpdateGeneralSupportCommandHandler(
    IGeneralSupportRepository generalSupports,
    IUnitRepository units,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateGeneralSupportCommand>
{
    public async Task<Result> Handle(UpdateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var id = GeneralSupportId.From(request.Id);
        var entity = await generalSupports.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("General support was not found.");

        var unitId = UnitId.From(request.UnitId);
        if (!await units.ExistsActiveByIdAsync(unitId, cancellationToken))
            return Error.Validation("The selected unit does not exist or is inactive.");

        if (await generalSupports.ExistsByNameAsync(request.Name, id, cancellationToken))
            return Error.Conflict("Another general support with this name already exists.");

        var detailsResult = entity.UpdateDetails(request.Name, unitId, request.IsDuration, request.Note);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        generalSupports.Update(entity);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.GeneralSupports);
        return Result.Success();
    }
}
