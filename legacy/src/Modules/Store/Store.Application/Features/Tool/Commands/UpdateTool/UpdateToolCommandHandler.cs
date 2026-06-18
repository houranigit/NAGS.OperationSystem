using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Commands.UpdateTool;

public sealed class UpdateToolCommandHandler(
    IToolRepository tools,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateToolCommand>
{
    public async Task<Result> Handle(UpdateToolCommand request, CancellationToken cancellationToken)
    {
        var id = ToolId.From(request.Id);
        var entity = await tools.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Tool was not found.");

        if (await tools.ExistsByNameAsync(request.Name, id, cancellationToken))
            return Error.Conflict("Another tool with this name already exists.");

        var detailsResult = entity.UpdateDetails(request.Name, request.Description);
        if (detailsResult.IsFailure) return detailsResult;

        var input = request.Equipments ?? [];
        var inputIds = input
            .Where(e => e.Id.HasValue)
            .Select(e => EquipmentId.From(e.Id!.Value))
            .ToHashSet();

        var existingIds = entity.Equipments.Select(e => e.Id).ToList();
        foreach (var equipmentId in existingIds.Where(eid => !inputIds.Contains(eid)))
        {
            var remove = entity.RemoveEquipment(equipmentId);
            if (remove.IsFailure) return remove;
        }

        foreach (var equipment in input)
        {
            if (equipment.Id is { } existingId)
            {
                var update = entity.UpdateEquipment(
                    EquipmentId.From(existingId),
                    equipment.FactoryId,
                    equipment.SerialId,
                    equipment.CalibrationDate);
                if (update.IsFailure) return update;
            }
            else
            {
                var add = entity.AddEquipment(equipment.FactoryId, equipment.SerialId, equipment.CalibrationDate);
                if (add.IsFailure) return add.Error;
            }
        }

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        tools.Update(entity);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Tools);
        return Result.Success();
    }
}
