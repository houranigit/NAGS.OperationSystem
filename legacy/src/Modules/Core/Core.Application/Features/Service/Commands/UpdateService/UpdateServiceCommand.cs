using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.Service.Commands.UpdateService;

public sealed record UpdateServiceCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive) : ICommand;
