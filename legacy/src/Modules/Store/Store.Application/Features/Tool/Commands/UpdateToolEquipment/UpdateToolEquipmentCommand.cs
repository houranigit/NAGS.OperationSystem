using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Tool.Commands.UpdateToolEquipment;

public sealed record UpdateToolEquipmentCommand(
    Guid ToolId,
    Guid EquipmentId,
    string FactoryId,
    string SerialId,
    DateOnly? CalibrationDate) : ICommand;
