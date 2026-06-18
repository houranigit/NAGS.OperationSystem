using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Commands.AddToolEquipment;

public sealed class AddToolEquipmentCommandHandler(IToolRepository tools)
    : ICommandHandler<AddToolEquipmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddToolEquipmentCommand request, CancellationToken cancellationToken)
    {
        var toolId = ToolId.From(request.ToolId);
        var tool = await tools.GetByIdAsync(toolId, cancellationToken);
        if (tool is null) return Error.NotFound("Tool was not found.");

        var add = tool.AddEquipment(request.FactoryId, request.SerialId, request.CalibrationDate);
        if (add.IsFailure) return add.Error;

        tools.Update(tool);
        return add.Value.Value;
    }
}
