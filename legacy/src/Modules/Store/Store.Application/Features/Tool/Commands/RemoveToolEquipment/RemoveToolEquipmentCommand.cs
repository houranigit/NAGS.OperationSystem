using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Tool.Commands.RemoveToolEquipment;

public sealed record RemoveToolEquipmentCommand(Guid ToolId, Guid EquipmentId) : ICommand;
