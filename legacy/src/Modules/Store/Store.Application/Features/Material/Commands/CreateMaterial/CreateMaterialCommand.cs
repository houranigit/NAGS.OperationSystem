using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Material.Commands.CreateMaterial;

public sealed record CreateMaterialCommand(
    string Name,
    Guid UnitId,
    bool IsActive) : ICommand<Guid>;
