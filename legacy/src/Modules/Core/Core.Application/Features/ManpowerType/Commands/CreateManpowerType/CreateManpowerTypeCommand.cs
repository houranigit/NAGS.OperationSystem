using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.ManpowerType.Commands.CreateManpowerType;

public sealed record CreateManpowerTypeCommand(
    string Name,
    string? Description,
    bool IsActive) : ICommand<Guid>;
