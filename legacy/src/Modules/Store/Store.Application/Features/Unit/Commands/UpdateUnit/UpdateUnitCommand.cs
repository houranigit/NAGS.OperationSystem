using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Unit.Commands.UpdateUnit;

public sealed record UpdateUnitCommand(
    Guid Id,
    string Code,
    string Name,
    bool IsActive) : ICommand;
