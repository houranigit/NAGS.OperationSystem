using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.Service.Commands.CreateService;

public sealed record CreateServiceCommand(
    string Name,
    string? Description,
    bool IsActive) : ICommand<Guid>;
