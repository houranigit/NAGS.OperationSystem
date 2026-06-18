using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Tool.Commands.AddToolEquipment;

public sealed record AddToolEquipmentCommand(
    Guid ToolId,
    string FactoryId,
    string SerialId,
    DateOnly? CalibrationDate) : ICommand<Guid>;
