using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Material.Commands.UpdateMaterial;

public sealed record UpdateMaterialCommand(
    Guid Id,
    string Name,
    Guid UnitId,
    bool IsActive) : ICommand;
