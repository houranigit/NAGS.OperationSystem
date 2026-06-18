using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.OperationType.Commands.CreateOperationType;

public sealed record CreateOperationTypeCommand(
    string Name,
    string? Description,
    bool IsActive) : ICommand<Guid>;
