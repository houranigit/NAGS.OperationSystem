using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.Unit;

namespace Store.Application.Features.GeneralSupport.Commands.CreateGeneralSupport;

public sealed class CreateGeneralSupportCommandHandler(
    IGeneralSupportRepository generalSupports,
    IUnitRepository units,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateGeneralSupportCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var unitId = UnitId.From(request.UnitId);
        if (!await units.ExistsActiveByIdAsync(unitId, cancellationToken))
            return Error.Validation("The selected unit does not exist or is inactive.");

        if (await generalSupports.ExistsByNameAsync(request.Name, ct: cancellationToken))
            return Error.Conflict("A general support with this name already exists.");

        var created = Store.Domain.Aggregates.GeneralSupport.GeneralSupport.Create(
            request.Name, unitId, request.IsDuration, request.Note);
        if (created.IsFailure) return created.Error;

        var item = created.Value;

        if (!request.IsActive)
        {
            var d = item.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        generalSupports.Add(item);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.GeneralSupports);
        return item.Id.Value;
    }
}
