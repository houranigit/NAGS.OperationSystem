using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.OperationType.Commands.UpdateOperationType;

public sealed record UpdateOperationTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive) : ICommand;
