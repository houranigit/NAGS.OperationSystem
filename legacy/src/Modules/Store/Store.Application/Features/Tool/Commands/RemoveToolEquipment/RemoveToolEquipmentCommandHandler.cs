using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Commands.RemoveToolEquipment;

public sealed class RemoveToolEquipmentCommandHandler(IToolRepository tools)
    : ICommandHandler<RemoveToolEquipmentCommand>
{
    public async Task<Result> Handle(RemoveToolEquipmentCommand request, CancellationToken cancellationToken)
    {
        var toolId = ToolId.From(request.ToolId);
        var tool = await tools.GetByIdAsync(toolId, cancellationToken);
        if (tool is null) return Error.NotFound("Tool was not found.");

        var remove = tool.RemoveEquipment(EquipmentId.From(request.EquipmentId));
        if (remove.IsFailure) return remove;

        tools.Update(tool);
        return Result.Success();
    }
}
