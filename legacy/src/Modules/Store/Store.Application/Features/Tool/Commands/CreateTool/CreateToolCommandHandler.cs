using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Commands.CreateTool;

public sealed class CreateToolCommandHandler(
    IToolRepository tools,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateToolCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateToolCommand request, CancellationToken cancellationToken)
    {
        if (await tools.ExistsByNameAsync(request.Name, ct: cancellationToken))
            return Error.Conflict("A tool with this name already exists.");

        var created = Store.Domain.Aggregates.Tool.Tool.Create(request.Name, request.Description);
        if (created.IsFailure) return created.Error;

        var tool = created.Value;

        foreach (var equipment in request.Equipments ?? [])
        {
            var add = tool.AddEquipment(equipment.FactoryId, equipment.SerialId, equipment.CalibrationDate);
            if (add.IsFailure) return add.Error;
        }

        if (!request.IsActive)
        {
            var d = tool.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        tools.Add(tool);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Tools);
        return tool.Id.Value;
    }
}
