using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.ManpowerType.Commands.UpdateManpowerType;

public sealed record UpdateManpowerTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive) : ICommand;
