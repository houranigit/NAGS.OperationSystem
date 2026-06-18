using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Commands.UpdateToolEquipment;

public sealed class UpdateToolEquipmentCommandHandler(IToolRepository tools)
    : ICommandHandler<UpdateToolEquipmentCommand>
{
    public async Task<Result> Handle(UpdateToolEquipmentCommand request, CancellationToken cancellationToken)
    {
        var toolId = ToolId.From(request.ToolId);
        var tool = await tools.GetByIdAsync(toolId, cancellationToken);
        if (tool is null) return Error.NotFound("Tool was not found.");

        var update = tool.UpdateEquipment(
            EquipmentId.From(request.EquipmentId),
            request.FactoryId,
            request.SerialId,
            request.CalibrationDate);
        if (update.IsFailure) return update;

        tools.Update(tool);
        return Result.Success();
    }
}
