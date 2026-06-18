using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.Unit.Commands.CreateUnit;

public sealed record CreateUnitCommand(
    string Code,
    string Name,
    bool IsActive) : ICommand<Guid>;
