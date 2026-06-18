using BuildingBlocks.Application.Abstractions.Commands;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Commands.UpdateTool;

/// <summary>
/// Updates the tool's basic fields, active flag, and the entire equipments collection. Equipments
/// without an Id are added; existing rows are updated; rows present on the aggregate but missing
/// from the input are removed.
/// </summary>
public sealed record UpdateToolCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<ToolEquipmentInput> Equipments) : ICommand;
