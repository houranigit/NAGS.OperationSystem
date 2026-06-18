using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Commands.CreateTool;

public sealed record CreateToolCommand(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<ToolEquipmentInput> Equipments) : ICommand<Guid>;
